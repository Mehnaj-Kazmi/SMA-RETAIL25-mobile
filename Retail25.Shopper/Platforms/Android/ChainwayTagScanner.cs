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
    private bool _unavailable;

    public bool IsAvailable
    {
        get
        {
            EnsureReader();
            return _initialised;
        }
    }

    public async Task<IReadOnlyList<string>> SweepAsync(TimeSpan window, CancellationToken ct = default)
    {
        EnsureReader();

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
            if (_initialised || _unavailable)
            {
                return;
            }

            try
            {
                var readerClass = JNIEnv.FindClass(ReaderClass);
                var getInstance = JNIEnv.GetStaticMethodID(readerClass, "getInstance", $"()L{ReaderClass};");
                var instance = JNIEnv.CallStaticObjectMethod(readerClass, getInstance);

                if (instance == IntPtr.Zero)
                {
                    _unavailable = true;
                    return;
                }

                // A global reference: this handle outlives the call that made it, and a local ref
                // is void the moment control returns to Java.
                _reader = JNIEnv.NewGlobalRef(instance);
                JNIEnv.DeleteLocalRef(instance);

                var init = JNIEnv.GetMethodID(readerClass, "init", "(Landroid/content/Context;)Z");
                var context = global::Android.App.Application.Context.Handle;

                _initialised = JNIEnv.CallBooleanMethod(_reader, init, new JValue(context));
                _unavailable = !_initialised;
            }
            catch (Exception)
            {
                // No reader on this device, or the SDK could not load its natives.
                _unavailable = true;
                _initialised = false;
            }
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
