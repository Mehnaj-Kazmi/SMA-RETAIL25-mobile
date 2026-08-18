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
    /// Hands over to the counter screen, where the shopper says which counter they are standing at.
    /// <para>
    /// Signing in no longer picks a counter on the customer's behalf. Being issued one silently is
    /// fine only while every counter is interchangeable, and they are not: the RFID reader is bolted
    /// to a particular counter, so a shopper standing at 307 who is handed 305 watches a basket
    /// filling somewhere else in the shop. Choosing is one screen and removes the whole class of
    /// problem — with "give me any free one" still on that screen for a shop where it genuinely does
    /// not matter.
    /// </para>
    /// </summary>
    private async Task OpenCounterAsync()
        => await Shell.Current.GoToAsync(nameof(PairTrolleyPage));

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
