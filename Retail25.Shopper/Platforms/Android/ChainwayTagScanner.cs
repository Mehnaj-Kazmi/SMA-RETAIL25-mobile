using Android.Runtime;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Platforms.Android;

/// <summary>
/// The Chainway C72's onboard UHF module, driven through Chainway's DeviceAPI.
/// <para>
/// Called over JNI rather than a generated binding. The .aar covers Chainway's whole handheld range
/// and will not project into C# as a whole — see the note on the AndroidLibrary item in the csproj.
/// This app needs five methods on one class, so it asks for those five directly, against signatures
/// read out of the .aar with <c>javap</c>:
/// </para>
/// <code>
///   RFIDWithUHFUART.getInstance()      ()Lcom/rscja/deviceapi/RFIDWithUHFUART;
///                   .init(Context)     (Landroid/content/Context;)Z
///                   .startInventoryTag ()Z
///                   .readTagFromBuffer ()Lcom/rscja/deviceapi/entity/UHFTAGInfo;
///                   .stopInventory     ()Z
///   UHFTAGInfo.getEPC()                ()Ljava/lang/String;
/// </code>
/// <para>
/// Inventory rather than a single read, because the point of UHF is that a basket is one action. A
/// trigger pull sweeps for a window and returns every distinct tag in the field, which is also the
/// right shape for one item held against the antenna — it just returns a list of one.
/// </para>
/// </summary>
public sealed class ChainwayTagScanner : ITagScanner, IDisposable
{
    private const string ReaderClass = "com/rscja/deviceapi/RFIDWithUHFUART";
    private const string TagInfoClass = "com/rscja/deviceapi/entity/UHFTAGInfo";

    /// <summary>
    /// How long to wait for the next tag before deciding the field is empty.
    /// <para>
    /// readTagFromBuffer returns null between reads as well as when there is nothing there, so a
    /// sweep cannot stop at the first null — it would miss the second item in a basket. It stops
    /// when nothing has arrived for this long, or when the caller's window expires.
    /// </para>
    /// </summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(350);

    private readonly object _gate = new();

    private IntPtr _reader;
    private bool _initialised;

    /// <summary>
    /// Whether the radio is up. Reports what is known now and never itself provokes a connection
    /// attempt: a getter that blocks on UART initialisation is a getter that freezes the UI thread
    /// when the module is slow, which is exactly when somebody is looking at the screen.
    /// </summary>
    public bool IsAvailable => _initialised;

    public async Task<IReadOnlyList<string>> SweepAsync(TimeSpan window, CancellationToken ct = default)
    {
        // Off the UI thread. Bringing the UART up takes the better part of a second on this module,
        // and the vendor's own demo does it on a background task for the same reason.
        await Task.Run(EnsureReader, ct).ConfigureAwait(false);

        if (!_initialised)
        {
            return [];
        }

        // Ordinal: an EPC is a hex string, and two that differ only by case are the same tag.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!CallBool(_reader, ReaderClass, "startInventoryTag"))
        {
            return [];
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow + window;
            var lastRead = DateTimeOffset.UtcNow;

            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var epc = ReadOne();

                if (epc is { Length: > 0 })
                {
                    seen.Add(epc);
                    lastRead = DateTimeOffset.UtcNow;
                }
                else if (DateTimeOffset.UtcNow - lastRead > QuietPeriod && seen.Count > 0)
                {
                    // Something was read and the field has gone quiet: the basket is counted.
                    break;
                }

                // The module fills its buffer on its own schedule; polling flat out burns battery
                // on a device that spends its whole shift in someone's hand.
                await Task.Delay(30, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            CallBool(_reader, ReaderClass, "stopInventory");
        }

        return [.. seen];
    }

    /// <summary>One tag out of the module's buffer, or null when there was nothing waiting.</summary>
    private string? ReadOne()
    {
        var readerClass = JNIEnv.FindClass(ReaderClass);
        var method = JNIEnv.GetMethodID(readerClass, "readTagFromBuffer", $"()L{TagInfoClass};");
        var info = JNIEnv.CallObjectMethod(_reader, method);

        if (info == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var infoClass = JNIEnv.FindClass(TagInfoClass);
            var getEpc = JNIEnv.GetMethodID(infoClass, "getEPC", "()Ljava/lang/String;");
            var epc = JNIEnv.CallObjectMethod(info, getEpc);

            return epc == IntPtr.Zero ? null : JNIEnv.GetString(epc, JniHandleOwnership.TransferLocalRef);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(info);
        }
    }

    /// <summary>
    /// Gets the module powered up, once.
    /// <para>
    /// Failure here is expected rather than exceptional — this same APK runs on ordinary phones for
    /// testing, where the class is absent and the natives will not load. It is recorded and the
    /// scanner reports itself unavailable, which the cart screen already handles by falling back to
    /// typed entry.
    /// </para>
    /// </summary>
    private void EnsureReader()
    {
        lock (_gate)
        {
            if (_initialised)
            {
                return;
            }

            try
            {
                if (_reader == IntPtr.Zero)
                {
                    var readerClass = JNIEnv.FindClass(ReaderClass);
                    var getInstance = JNIEnv.GetStaticMethodID(readerClass, "getInstance", $"()L{ReaderClass};");
                    var instance = JNIEnv.CallStaticObjectMethod(readerClass, getInstance);

                    if (instance == IntPtr.Zero)
                    {
                        return;
                    }

                    // A global reference: this handle outlives the call that made it, and a local
                    // ref is void the moment control returns to Java.
                    _reader = JNIEnv.NewGlobalRef(instance);
                    JNIEnv.DeleteLocalRef(instance);
                }

                if (TryInit())
                {
                    _initialised = true;
                    return;
                }

                // Failed. The usual cause is the module still being held from a previous run: the
                // app was force-stopped or crashed, so free() never ran and the UART is claimed by a
                // process that no longer exists. Releasing it first is what makes this recoverable
                // without rebooting the handset — which is otherwise the only cure, and not one you
                // can ask a shop to apply mid-shift.
                CallBool(_reader, ReaderClass, "free");
                Thread.Sleep(300);

                _initialised = TryInit();
            }
            catch (Exception)
            {
                // No reader on this device, or the SDK could not load its natives. Not latched: the
                // next trigger pull tries again, because the reason can be temporary and a scanner
                // that gives up permanently on one bad start is a scanner somebody reboots.
                _initialised = false;
            }
        }
    }

    /// <summary>
    /// Brings the module up, preferring the overload the vendor's own demo uses.
    /// <para>
    /// <c>init()</c> first and <c>init(Context)</c> only as a fallback: the demo shipped with this
    /// SDK calls the no-argument form, and on this handset it is the one that works. They are not
    /// aliases — the context overload exists for models where the SDK powers the radio through a
    /// system service — so trying both costs one call and covers both wirings.
    /// </para>
    /// </summary>
    private bool TryInit()
    {
        var readerClass = JNIEnv.FindClass(ReaderClass);

        if (CallBool(_reader, ReaderClass, "init"))
        {
            return true;
        }

        try
        {
            var init = JNIEnv.GetMethodID(readerClass, "init", "(Landroid/content/Context;)Z");
            var context = global::Android.App.Application.Context.Handle;

            return JNIEnv.CallBooleanMethod(_reader, init, new JValue(context));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CallBool(IntPtr instance, string className, string method)
    {
        try
        {
            var cls = JNIEnv.FindClass(className);
            return JNIEnv.CallBooleanMethod(instance, JNIEnv.GetMethodID(cls, method, "()Z"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_reader == IntPtr.Zero)
            {
                return;
            }

            // free() powers the module down. Skipping it leaves the radio drawing current after the
            // app is gone, which on a handheld is a flat battery by the middle of a shift.
            CallBool(_reader, ReaderClass, "free");

            JNIEnv.DeleteGlobalRef(_reader);
            _reader = IntPtr.Zero;
            _initialised = false;
        }
    }
}
