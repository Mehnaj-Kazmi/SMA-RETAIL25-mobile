using Retail25.Shopper.Pages;

namespace Retail25.Shopper;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Registered rather than declared as ShellContent, so these are pushed onto a navigation
        // stack and "back" means back. As tabs they would each be a root, and the back arrow drawn on
        // sign-up would have nowhere to go.
        Routing.RegisterRoute(nameof(SignUpPage), typeof(SignUpPage));
        Routing.RegisterRoute(nameof(SignInPage), typeof(SignInPage));
        Routing.RegisterRoute(nameof(PairTrolleyPage), typeof(PairTrolleyPage));
        Routing.RegisterRoute(nameof(CartPage), typeof(CartPage));
        Routing.RegisterRoute(nameof(PreviousSalesPage), typeof(PreviousSalesPage));
    }
}
