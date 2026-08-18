using Retail25.Shopper.Drawing;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

public partial class PairTrolleyPage : ContentPage
{
    /// <summary>
    /// Room for three digits, but one is enough to connect.
    /// <para>
    /// Staff know these counters by id â€” 3 to 22 â€” while the number printed on the counter is the
    /// three-digit code. Both work, so the field cannot insist on a fixed length: demanding three
    /// digits would reject "3", which is the number most of the shop actually says out loud.
    /// </para>
    /// </summary>
    private const int CodeLength = 3;

    /// <summary>
    /// The full three digits. A counter code is 301 through 320, and the counter displays all three,
    /// so accepting a partial number would only invite a connection attempt that cannot succeed.
    /// </summary>
    private const int MinimumCodeLength = CodeLength;

    private static readonly Color BoxEdge = Color.FromArgb("#8CFFFFFF");
    private static readonly Color BoxEdgeLit = Colors.White;
    private static readonly Color BoxFill = Color.FromArgb("#66FFFFFF");
    private static readonly Color BoxFillLit = Color.FromArgb("#B8FFFFFF");

    private readonly Label[] _digits;
    private readonly BoxView[] _carets;
    private readonly Border[] _boxes;
    private readonly ShopperApi _api = new();

    private bool _busy;

    public PairTrolleyPage()
    {
        InitializeComponent();

        TrolleyView.Drawable = new TrolleyMark();

        _boxes = [Box0, Box1, Box2];
        _digits = [Digit0, Digit1, Digit2];
        _carets = [Caret0, Caret1, Caret2];

        Paint(string.Empty);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // No automatic keyboard, and this was arrived at the hard way.
        //
        // Opening it on appearing saves the shopper one tap and costs them the three buttons it
        // draws itself over — including Connect, the one most of them want. Making that conditional
        // on the field being empty does not fix it: the counter code arrives from the server, the
        // focus was scheduled before that reply, and whenever the network is the slower of the two
        // the keyboard opens after the code has landed. A race decided by latency is not a design.
        //
        // So the shopper taps the digit boxes when they want to type — OnFocusCode opens the
        // keyboard then — and it closes itself on the third digit. The buttons are always reachable,
        // which matters more on this screen than saving a tap.

        // Say up front where they already are.
        //
        // A session survives the app closing, so somebody who shopped earlier arrives here still
        // connected. Left unsaid, the first thing they learn is a refusal after typing a number.
        // Filling the field with the counter they are on turns the primary button into "resume",
        // and typing over it is how they switch.
        var current = await _api.GetMyCartAsync();

        if (current.Ok && current.Value?.TrolleyCode is { Length: > 0 } connected)
        {
            CodeEntry.Text = connected;
            StatusLabel.Text = $"Still on counter {connected} — Connect resumes it, or type another number.";

            // The focus above was scheduled before this lookup returned, so by the time the counter
            // arrives the keyboard is already open over the button this shopper came to press. Close
            // it now that there is nothing left to type.
            DismissKeyboard();
        }
    }

    /// <summary>
    /// Closes the soft keyboard, properly.
    /// <para>
    /// <c>Unfocus()</c> is not enough on Android: it drops the caret but leaves the keyboard on
    /// screen, so the buttons stay covered and taps aimed at Connect land on number keys instead —
    /// which reads as an app that has frozen. Found by logging key events on the handheld and seeing
    /// a digit arrive from a tap meant for a button.
    /// </para>
    /// </summary>
    private void DismissKeyboard()
    {
        CodeEntry.Unfocus();

#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var token = activity?.CurrentFocus?.WindowToken;

        if (activity?.GetSystemService(global::Android.Content.Context.InputMethodService)
            is global::Android.Views.InputMethods.InputMethodManager manager && token is not null)
        {
            manager.HideSoftInputFromWindow(token, global::Android.Views.InputMethods.HideSoftInputFlags.None);
        }
#endif
    }

    private void OnFocusCode(object? sender, TappedEventArgs e) => CodeEntry.Focus();

    private void OnCodeChanged(object? sender, TextChangedEventArgs e)
    {
        // Numeric keyboards still admit a paste, and some IMEs send separators, so what arrives is
        // filtered rather than trusted.
        var digits = new string((e.NewTextValue ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (digits.Length > CodeLength)
        {
            digits = digits[..CodeLength];
        }

        if (digits != e.NewTextValue)
        {
            CodeEntry.Text = digits;
            return; // Re-enters with the cleaned value; painting once avoids a visible flicker.
        }

        Paint(digits);

        // The code is complete, so the keyboard has nothing left to contribute — and while it is up
        // it covers Connect and the two buttons under it. A shopper who has typed 302 and cannot see
        // the button that uses it will tap where the button is drawn and hit a number key instead,
        // which is exactly what happened when this was tested on the handheld.
        if (digits.Length == CodeLength)
        {
            DismissKeyboard();
        }
    }

    private void Paint(string code)
    {
        for (var i = 0; i < CodeLength; i++)
        {
            var filled = i < code.Length;
            var active = i == code.Length;

            _digits[i].Text = filled ? code[i].ToString() : string.Empty;
            _carets[i].IsVisible = active;

            _boxes[i].Stroke = filled || active ? BoxEdgeLit : BoxEdge;
            _boxes[i].BackgroundColor = filled || active ? BoxFillLit : BoxFill;
        }

        ConnectButton.IsEnabled = code.Length >= MinimumCodeLength && !_busy;

        // Editing the code clears whatever the last attempt said, colour included â€” leaving a red
        // "someone is already shopping with this trolley" under a freshly typed number would be
        // describing the wrong trolley.
        StatusLabel.TextColor = Color.FromArgb("#B8FFFFFF");

        StatusLabel.Text = code.Length >= MinimumCodeLength
            ? $"Counter {code}"
            : "The number is displayed at the counter.";
    }

    private async void OnConnect(object? sender, EventArgs e)
    {
        var code = CodeEntry.Text ?? string.Empty;

        if (_busy || code.Length < MinimumCodeLength)
        {
            return;
        }

        SetBusy(true);
        var result = await _api.ClaimTrolleyAsync(code);

        // Already connected somewhere else.
        //
        // A session deliberately outlives the app being closed — that is what lets a shopper who
        // locked their phone mid-shop come back to a full basket. But it also means the next thing
        // they see is a refusal, for a rule they never agreed to and cannot act on. Offering the
        // switch turns a dead end into the thing they were plainly trying to do.
        if (!result.Ok && string.Equals(result.Code, "trolley_session.already_shopping", StringComparison.Ordinal))
        {
            var current = await _api.GetMyCartAsync();
            var connectedTo = current.Value?.TrolleyCode;

            SetBusy(false);

            var move = await DisplayAlertAsync(
                connectedTo is { Length: > 0 }
                    ? $"You are on counter {connectedTo}"
                    : "Already connected",
                $"Leave it and connect to counter {code} instead? Anything in the old basket stays there for staff.",
                "Switch",
                "Stay");

            if (!move)
            {
                return;
            }

            SetBusy(true);
            await _api.ReleaseTrolleyAsync();
            result = await _api.ClaimTrolleyAsync(code);
        }

        SetBusy(false);

        if (!result.Ok || result.Value is null)
        {
            // The server's own words. "Someone else is already using that counter" and "no counter has
            // that number" need completely different things from the shopper, and only the server
            // knows which happened.
            StatusLabel.Text = result.Message ?? "Could not connect to that counter.";
            StatusLabel.TextColor = Color.FromArgb("#FFE2E2");
            return;
        }

        StatusLabel.TextColor = Color.FromArgb("#B8FFFFFF");

        // Straight through to the point-of-sale screen, which opens its live connection on appearing.
        await Shell.Current.GoToAsync($"{nameof(CartPage)}?code={result.Value.TrolleyCode}");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        // Both conditions, not just the busy flag. Re-enabling on "not busy" alone left Connect live
        // after a finished attempt even once the shopper had cleared the boxes â€” so the button invited
        // a tap that could only ever send an empty code to the server.
        ConnectButton.IsEnabled = !busy && (CodeEntry.Text?.Length ?? 0) >= MinimumCodeLength;
        ConnectButton.Text = busy ? "Connectingâ€¦" : "Connect";
    }

    /// <summary>
    /// Takes whichever counter the shop has free, for a store where they are interchangeable.
    /// <para>
    /// Lands on the same cart screen as typing a number, because it is the same claim underneath —
    /// the shop picks the code instead of the customer. A shopper already mid-trip gets that trip
    /// back rather than a second counter, so this is safe to press twice.
    /// </para>
    /// </summary>
    private async void OnAnyCounter(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        AnyCounterButton.IsEnabled = false;
        AnyCounterButton.Text = "Finding a counter…";

        var result = await _api.StartSelfCheckoutAsync();

        _busy = false;
        AnyCounterButton.IsEnabled = true;
        AnyCounterButton.Text = "Use Any Free Counter";

        if (!result.Ok || result.Value is null)
        {
            StatusLabel.Text = result.Message ?? "No counter is free just now.";
            StatusLabel.TextColor = Color.FromArgb("#FFE2E2");
            return;
        }

        StatusLabel.TextColor = Color.FromArgb("#B8FFFFFF");

        await Shell.Current.GoToAsync($"{nameof(CartPage)}?code={result.Value.TrolleyCode}");
    }

    private async void OnScanQr(object? sender, EventArgs e)
        => await DisplayAlertAsync("Not built yet", "Scanning the trolley's QR code is still to come.", "OK");

    private async void OnBack(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..");
}

