namespace Retail25.Shopper.Pages;

public partial class WelcomePage : ContentPage
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnCreateAccount(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SignUpPage));

    private async void OnSignIn(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SignInPage));
}
