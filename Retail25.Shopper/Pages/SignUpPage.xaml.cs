using Retail25.Shopper.Drawing;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

public partial class SignUpPage : ContentPage
{
    private readonly EyeIcon _eye = new();
    private readonly ShopperApi _api = new();

    private bool _busy;

    public SignUpPage()
    {
        InitializeComponent();
        EyeView.Drawable = _eye;
    }

    private async void OnBack(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnTogglePassword(object? sender, TappedEventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        _eye.Struck = !PasswordEntry.IsPassword;
        EyeView.Invalidate();
    }

    private async void OnCreateAccount(object? sender, EventArgs e)
    {
        // Guarded rather than relying on the disabled button: a double tap can queue two clicks
        // before the first one has had a chance to disable anything, and the result is two accounts
        // attempted for one person.
        if (_busy)
        {
            return;
        }

        // Checked here as well as on the server, and the rules are deliberately the same ones
        // Shopper.Create applies. This is for the shopper's benefit — a mistyped address should be
        // caught before a round trip, standing in a shop on a slow connection. It is not a security
        // boundary: the server validates independently and is the only opinion that counts.
        var problem = Validate();

        if (problem is not null)
        {
            ShowError(problem);
            return;
        }

        SetBusy(true);

        var result = await _api.RegisterAsync(
            FirstNameEntry.Text!.Trim(),
            LastNameEntry.Text!.Trim(),
            PhoneEntry.Text!.Trim(),
            EmailEntry.Text!.Trim(),
            PasswordEntry.Text!);

        SetBusy(false);

        if (!result.Ok || result.Value is null)
        {
            ShowError(result.Message ?? "Could not create your account.");
            return;
        }

        await SessionStore.AdoptAsync(result.Value);

        ErrorLabel.IsVisible = false;

        // Self-checkout: the shop issues the counter. A new customer goes from "create account"
        // straight to a basket without being asked for a number they have no way of knowing.
        SetBusy(true);

        var counter = await _api.StartSelfCheckoutAsync();

        SetBusy(false);

        if (!counter.Ok || counter.Value is null)
        {
            ShowError(counter.Message ?? "Could not open a self-checkout counter for you.");
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(CartPage)}?code={counter.Value.TrolleyCode}");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SubmitButton.IsEnabled = !busy;
        SubmitButton.Text = busy ? "Creating account…" : "Create Account";
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstNameEntry.Text) || string.IsNullOrWhiteSpace(LastNameEntry.Text))
        {
            return "Enter your first and last name.";
        }

        var digits = (PhoneEntry.Text ?? string.Empty).Count(char.IsAsciiDigit);

        if (digits is < 7 or > 20)
        {
            return "Enter a phone number we can reach you on.";
        }

        var email = (EmailEntry.Text ?? string.Empty).Trim();
        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at != email.LastIndexOf('@') || !email[(at + 1)..].Contains('.', StringComparison.Ordinal))
        {
            return "That does not look like an email address.";
        }

        if ((PasswordEntry.Text ?? string.Empty).Length < 8)
        {
            return "Your password needs at least 8 characters.";
        }

        return null;
    }
}
