using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

public partial class WelcomePage : ContentPage
{
    private readonly ShopperApi _api = new();

    private bool _resumeAttempted;

    public WelcomePage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await ResumeAsync();
    }

    /// <summary>
    /// Puts a returning shopper back where they were, without asking for a password again.
    /// <para>
    /// The refresh token outlives the process — it is in the Android Keystore, not in memory — so a
    /// customer who closed the app mid-shop, or whose handheld went flat, has everything needed to
    /// carry on. Making them retype a password to get back to a basket they are standing next to is
    /// the kind of friction that ends with the trolley abandoned in an aisle.
    /// </para>
    /// <para>
    /// Silent in both directions. There is no spinner and no message, because the honest outcomes are
    /// "you are already in" and "this screen, as before" — a failed resume is not an error the
    /// shopper did anything to cause, and telling them their session expired teaches them nothing
    /// they can act on. The token is cleared on the way out so a dead one is not retried for ever.
    /// </para>
    /// </summary>
    private async Task ResumeAsync()
    {
        // Once per launch. OnAppearing fires again every time the shopper backs out of sign-in, and
        // a resume that already failed will fail identically.
        if (_resumeAttempted)
        {
            return;
        }

        _resumeAttempted = true;

        var refreshToken = await SessionStore.ReadRefreshTokenAsync();

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var session = await _api.RefreshAsync(refreshToken);

        if (!session.Ok || session.Value is null)
        {
            // Expired, revoked, or the device row was cleared by staff. Forget it rather than carry a
            // credential that cannot work.
            SessionStore.Forget();
            return;
        }

        await SessionStore.AdoptAsync(session.Value);

        // To the counter screen, not straight to a basket: the shopper may well be at a different
        // counter than last time, and PairTrolleyPage already fills in the one they are still on and
        // turns Connect into "resume".
        await Shell.Current.GoToAsync(nameof(PairTrolleyPage));
    }

    private async void OnCreateAccount(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SignUpPage));

    private async void OnSignIn(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SignInPage));
}
