using Android.App;
using Android.Content.PM;
using Android.OS;
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
}
