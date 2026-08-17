using Retail25.Shopper.Drawing;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

public partial class SignInPage : ContentPage
{
    private readonly EyeIcon _eye = new();
    private readonly ShopperApi _api = new();

    private bool _busy;

    public SignInPage()
    {
        InitializeComponent();
        EyeView.Drawable = _eye;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Greeted by name where we have one. The name comes from the last successful sign-in on this
        // handset, not from anything sensitive, so there is nothing to protect behind a lock here.
        if (SessionStore.FirstName is { Length: > 0 } who)
        {
            WelcomeHeading.Text = $"WELCOME\nBACK, {who.ToUpperInvariant()}";
        }
    }

    private async void OnBack(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnTogglePassword(object? sender, TappedEventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        _eye.Struck = !PasswordEntry.IsPassword;
        EyeView.Invalidate();
    }

    private async void OnSignIn(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EmailEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
        {
            ShowError("Enter your email and password.");
            return;
        }

        SetBusy(true, "Signing in…");

        var result = await _api.SignInAsync(
            EmailEntry.Text.Trim(),
            PasswordEntry.Text);

        SetBusy(false, "Sign In");

        if (!result.Ok || result.Value is null)
        {
            ShowError(result.Message ?? "Could not sign you in.");
            return;
        }

        await SessionStore.AdoptAsync(result.Value);

        ErrorLabel.IsVisible = false;

        await OpenCounterAsync();
    }

    /// <summary>
    /// Gets the shopper onto a self-checkout counter and through to their basket.
    /// <para>
    /// The counter is issued by the shop, not chosen by the customer — signing in is the whole
    /// interaction. It is done here rather than on the cart screen because a failure has to land
    /// somewhere the shopper can act on it; a cart screen with no counter behind it has nothing to
    /// show and nothing to press.
    /// </para>
    /// </summary>
    private async Task OpenCounterAsync()
    {
        SetBusy(true, "Opening a counter…");

        var counter = await _api.StartSelfCheckoutAsync();

        SetBusy(false, "Sign In");

        if (!counter.Ok || counter.Value is null)
        {
            ShowError(counter.Message ?? "Could not open a self-checkout counter for you.");
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(CartPage)}?code={counter.Value.TrolleyCode}");
    }

    private async void OnForgotPassword(object? sender, EventArgs e)
        => await DisplayAlertAsync("Not built yet", "Password recovery is still to come.", "OK");

    private void SetBusy(bool busy, string label)
    {
        _busy = busy;
        SubmitButton.IsEnabled = !busy;
        SubmitButton.Text = label;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
