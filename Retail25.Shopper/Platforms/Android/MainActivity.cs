using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Retail25.Shopper;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>Top of the sky gradient, and bottom of it. See Resources/Styles/Shopper.xaml.</summary>
    private const string GradientTop = "#CFECF5";
    private const string GradientBottom = "#2A2650";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is null)
        {
            return;
        }

        // Left alone, the system bars keep the theme's purple and sit on the design like two bands of
        // the wrong colour — the gradient visibly starts below the clock. Painting them the two ends
        // of the gradient makes the page read as full-bleed without drawing underneath the bars,
        // which would need every screen to carry its own inset padding for the sake of a few pixels.
        // Obsolete from API 35, where the replacement is true edge-to-edge with per-screen inset
        // padding. Kept deliberately: this app supports API 21 upward and the handhelds it is built
        // for run Android 11, where these are the only way to colour the bars. The call is a no-op
        // rather than a fault on 35+, so the newer devices simply keep the system default.
#pragma warning disable CA1422
        Window.SetStatusBarColor(Android.Graphics.Color.ParseColor(GradientTop));
        Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor(GradientBottom));
#pragma warning restore CA1422

        var bars = WindowCompat.GetInsetsController(Window, Window.DecorView);

        if (bars is not null)
        {
            // Dark icons on the pale top, light ones on the dark bottom.
            bars.AppearanceLightStatusBars = true;
            bars.AppearanceLightNavigationBars = false;
        }
    }

    /// <summary>
    /// Raised when the handheld's scan trigger is pulled. The cart screen listens.
    /// <para>
    /// An event rather than a service call because the trigger is a fact about the device, not about
    /// whatever screen happens to be open: only the screen that can act on it subscribes, and it is
    /// silently ignored everywhere else, which is what the physical button should do on a sign-in
    /// page.
    /// </para>
    /// </summary>
    public static event Action? TriggerPulled;

    /// <summary>
    /// The hardware scan code this handset's trigger sends.
    /// <para>
    /// Matched on the scan code rather than the key code, and that is the whole fix. The C72's
    /// trigger is its own input device — <c>scan-key</c> on /dev/input/event0 — emitting Linux key
    /// 0xBA (186). Android has no keylayout for that device, so it falls back to Generic.kl, where
    /// 186 is labelled <c>F16</c>; and Android's KeyEvent stops at F12, so the label resolves to
    /// nothing and the trigger arrives with no usable key code at all.
    /// </para>
    /// <para>
    /// The scan code survives that, because it is the raw number from the driver and never passes
    /// through the keylayout. 139 and 280 are kept alongside it: they come from the vendor's demo,
    /// they work on other models in the range, and one of them is what an <c>adb keyevent</c> sends
    /// when testing this path without the hardware.
    /// </para>
    /// </summary>
    private const int TriggerScanCode = 186;

    private static bool IsScanTrigger(Keycode keyCode, KeyEvent? e)
        => (int)keyCode is 139 or 280 || e?.ScanCode == TriggerScanCode;

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        // Every key, once, while diagnosing a trigger that does not arrive. Cheap and quiet: a
        // handheld sends a handful of key events an hour, and without it the only way to learn what
        // the button emits is to rebuild with logging — which is how this bug was found.
        Android.Util.Log.Info("Retail25.Trigger", $"keyCode={(int)keyCode} scanCode={e?.ScanCode} repeat={e?.RepeatCount}");

        // RepeatCount: holding the trigger autorepeats, and each repeat would start another sweep on
        // top of the one already running. Only the first press counts.
        if (IsScanTrigger(keyCode, e) && e?.RepeatCount == 0)
        {
            TriggerPulled?.Invoke();
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }

    /// <summary>
    /// Swallowed so the key never reaches the focused control. Without this the trigger types a
    /// character into whatever field has focus — including the scan box the sweep is about to fill.
    /// </summary>
    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e)
        => IsScanTrigger(keyCode, e) || base.OnKeyUp(keyCode, e);
}
