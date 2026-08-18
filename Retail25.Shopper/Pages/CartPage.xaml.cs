using System.Collections.ObjectModel;
using System.Globalization;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

/// <summary>One row as the list renders it, with the formatting already done.</summary>
public sealed class LineRow
{
    public required int Sequence { get; init; }

    public required string Name { get; init; }

    public required string Detail { get; init; }

    public required string Amount { get; init; }

    public bool IsFresh { get; set; }

    public string Flag => "JUST ADDED";
}

/// <summary>
/// The point-of-sale screen: what the counter has read, updating live.
/// <para>
/// Two sources feed it, and the split matters. The opening state is fetched once over HTTP, because
/// the live connection carries <em>changes</em> and cannot tell you what was already there. Every
/// change after that arrives over the WebSocket. Polling is never used.
/// </para>
/// </summary>
public partial class CartPage : ContentPage, IQueryAttributable
{
    private readonly ObservableCollection<LineRow> _rows = [];
    private readonly ShopperApi _api = new();
    private readonly LiveCart _live;

    /// <summary>
    /// The handheld's reader where there is one, and a scanner that finds nothing where there is
    /// not — an ordinary phone, or the emulator. The cart screen does not branch on which: the
    /// trigger simply never fires without hardware, and the typed box stays the way in.
    /// </summary>
    private readonly ITagScanner _scanner =
#if ANDROID
        new Platforms.Android.ChainwayTagScanner();
#else
        new NullTagScanner();
#endif

    /// <summary>The beep. A shop floor is loud and nobody watches the screen while scanning.</summary>
    private readonly IScanFeedback _feedback =
#if ANDROID
        new Platforms.Android.ScanFeedback();
#else
        new NullScanFeedback();
#endif

    private string _counterCode = string.Empty;
    private long _cartId;

    /// <summary>Guards against a second sweep starting while one is still running.</summary>
    private bool _scanning;

    public CartPage()
    {
        InitializeComponent();

        _live = new LiveCart(_api);
        LinesView.ItemsSource = _rows;

        _live.LinesAdded += OnLinesAdded;
        _live.TotalsChanged += ShowTotals;
        _live.CartReplaced += ShowCart;
        _live.TagRejected += OnTagRejected;
        _live.StateChanged += OnConnectionState;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TryGetValue("code", out var code))
        {
            _counterCode = code?.ToString() ?? string.Empty;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Only this screen answers the trigger, and only while it is in front. Subscribing for the
        // app's lifetime would fire a sweep against a cart the shopper has already left.
#if ANDROID
        MainActivity.TriggerPulled += OnTriggerPulled;
#endif


        CounterLabel.Text = _counterCode.Length > 0
            ? $"COUNTER {_counterCode}"
            : "COUNTER";

        // The starting point, over HTTP. Without it a shopper who reopens the app mid-shop sees an
        // empty basket until the next tag happens to be read.
        var current = await _api.GetMyCartAsync();

        if (!current.Ok || current.Value?.Cart is null)
        {
            OnConnectionState("offline");
            return;
        }

        _cartId = current.Value.Cart.Id;

        if (current.Value.TrolleyCode is { Length: > 0 } claimed)
        {
            CounterLabel.Text = $"COUNTER {claimed}";
        }

        ShowCart(current.Value.Cart);

        await _live.StartAsync(_cartId);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

#if ANDROID
        MainActivity.TriggerPulled -= OnTriggerPulled;
#endif

        // Closed rather than left open in the background. A socket held by a screen nobody is looking
        // at is a socket the server is paying to keep alive.
        await _live.StopAsync();
    }

    private void ShowCart(Cart cart)
    {
        _rows.Clear();

        foreach (var line in cart.Lines)
        {
            _rows.Add(ToRow(line, fresh: false));
        }

        ShowTotals(cart.Totals);
    }

    private void OnLinesAdded(IReadOnlyList<CartLine> lines)
    {
        // Newest at the top, flagged. A shopper who has just put something down looks at the top of
        // the screen, not at the bottom of a list they would have to scroll.
        foreach (var row in _rows)
        {
            row.IsFresh = false;
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            // Deduplicated by sequence, because a line can arrive twice: once in the HTTP response
            // to this handheld's own scan — drawn immediately, the customer is watching — and again
            // when the hub broadcasts the same mutation to everyone watching the cart, this phone
            // included. Sequence is unique within the cart, so seen-once is exactly one check.
            if (_rows.Any(r => r.Sequence == lines[i].Sequence))
            {
                continue;
            }

            _rows.Insert(0, ToRow(lines[i], fresh: true));
        }

        CountLabel.Text = _rows.Count == 1 ? "1 item" : $"{_rows.Count} items";
    }

    private void ShowTotals(CartTotals totals)
    {
        TotalLabel.Text = Money(totals.GrandTotal);
        GrandTotalLabel.Text = Money(totals.GrandTotal);
        SubtotalLabel.Text = Money(totals.Subtotal);

        // Both tax bands folded into one line: the shopper is owed an accurate figure, not the
        // store's tax configuration.
        TaxNameLabel.Text = string.IsNullOrWhiteSpace(totals.Tax1Name) ? "Tax" : totals.Tax1Name;
        TaxLabel.Text = Money(totals.Tax1Total + totals.Tax2Total);

        CountLabel.Text = totals.ItemCount == 1 ? "1 item" : $"{totals.ItemCount} items";
        PayButton.Text = totals.GrandTotal > 0 ? $"Pay {Money(totals.GrandTotal)}" : "Pay";
    }

    private void OnTagRejected(RejectedTag rejected)
    {
        RejectTitle.Text = string.IsNullOrWhiteSpace(rejected.Message)
            ? "Tag not recognised"
            : rejected.Message;

        RejectDetail.Text = string.IsNullOrWhiteSpace(rejected.Reason)
            ? "Ask staff to add this item"
            : rejected.Reason;

        RejectBanner.IsVisible = true;
    }

    private void OnConnectionState(string state)
    {
        LiveDot.Fill = state switch
        {
            "connected" => new SolidColorBrush(Color.FromArgb("#7BE3A8")),
            "reconnecting" => new SolidColorBrush(Color.FromArgb("#FFD27A")),
            _ => new SolidColorBrush(Color.FromArgb("#FF9A9A")),
        };
    }

    private static LineRow ToRow(CartLine line, bool fresh)
    {
        var detail = $"{line.Quantity:0.##} × {Money(line.UnitPrice)}";

        if (!string.IsNullOrWhiteSpace(line.VariantLabel))
        {
            detail = $"{line.VariantLabel} · {detail}";
        }

        // The tag id, trimmed. Useful when an item is disputed and meaningless clutter at full length.
        if (line.Epc is { Length: > 8 } epc)
        {
            detail = $"{detail} · {epc[..4]}…{epc[^4..]}";
        }

        return new LineRow
        {
            Sequence = line.Sequence,
            Name = line.Name,
            Detail = detail,
            Amount = Money(line.ExtendedNet),
            IsFresh = fresh,
        };
    }

    private static string Money(decimal value)
        => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads whatever is in front of the antenna and puts it on the bill.
    /// <para>
    /// Reached two ways that are the same thing: the on-screen button and the handheld's physical
    /// trigger. A sweep rather than a single read, because the reason to use UHF over a barcode is
    /// that a whole basket is one action — one item held to the antenna is this same code returning
    /// a list of one.
    /// </para>
    /// </summary>
    private void OnTapToScan(object? sender, EventArgs e) => OnTriggerPulled();

    private async void OnTriggerPulled()
    {
        if (_scanning)
        {
            return;
        }

        // Said out loud rather than silently doing nothing. A button that responds to nothing is
        // read as a broken app, and on a device with no radio that is the only honest answer.
        if (!_scanner.IsAvailable)
        {
            OnTagRejected(new RejectedTag(
                string.Empty,
                "reader.unavailable",
                "This device has no tag reader. Use a store handheld to scan items."));

            return;
        }

        _scanning = true;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TapToScanButton.Text = "READING…";
                TapToScanButton.IsEnabled = false;
            });

            var epcs = await _scanner.SweepAsync(TimeSpan.FromSeconds(1.5));

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TapToScanButton.Text = "TAP TO SCAN";
                TapToScanButton.IsEnabled = true;
            });

            if (epcs.Count == 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() => OnTagRejected(new RejectedTag(
                    string.Empty,
                    "reader.nothing_in_range",
                    "Nothing in range — hold the handheld closer and pull the trigger again.")));

                return;
            }

            await SubmitAsync([.. epcs]);
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>
    /// Sends the tags the reader found to the shop and shows what came back — lines on the bill, or
    /// a refusal with its reason. Silence is never an outcome.
    /// </summary>
    private async Task SubmitAsync(string[] epcs)
    {
        var epc = epcs[0];

        var outcome = await _api.SubmitTagsAsync(epcs);

        if (!outcome.Ok || outcome.Value is null)
        {
            OnTagRejected(new RejectedTag(epc, "error", outcome.Message ?? "The shop could not read that tag."));
            return;
        }

        // Drawn from the response for immediacy; the hub will echo the same lines back and
        // OnLinesAdded drops them by sequence, so nothing appears twice.
        if (outcome.Value.Cart is { } cart)
        {
            ShowCart(cart);
            RejectBanner.IsVisible = false;
        }

        foreach (var refusal in outcome.Value.Rejected)
        {
            OnTagRejected(new RejectedTag(refusal.Epc, refusal.Reason, refusal.Message));
        }

        // Judged on whether anything was actually accepted, not on the HTTP call succeeding. A sweep
        // that reached the shop and had every tag refused is a failure to the person holding the
        // handheld, and beeping "yes" at them would be the sound lying about the outcome.
        if (outcome.Value.Accepted.Count > 0)
        {
            _feedback.Accepted();
        }
        else
        {
            _feedback.Refused();
        }
    }

    /// <summary>
    /// Empties the basket. Confirmed, and the confirmation names the count and the total, because
    /// "clear everything" is only safe to answer when you can see what everything is.
    /// </summary>
    private async void OnClearAll(object? sender, EventArgs e)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Empty the basket?",
            $"All {_rows.Count} item{(_rows.Count == 1 ? string.Empty : "s")} come off your bill — {GrandTotalLabel.Text}. Put them back on the shelf.",
            "Empty it",
            "Keep");

        if (!confirmed)
        {
            return;
        }

        var outcome = await _api.ClearCartAsync();

        if (!outcome.Ok || outcome.Value?.Cart is null)
        {
            OnTagRejected(new RejectedTag(
                string.Empty,
                "cart.not_cleared",
                outcome.Message ?? "The basket could not be emptied."));

            return;
        }

        ShowCart(outcome.Value.Cart);
        RejectBanner.IsVisible = false;
    }

    /// <summary>
    /// The customer puts an item back. Confirmed first — a mis-tap through a trolley handle should
    /// not silently shrink the bill — then the whole refreshed cart is drawn from the response, and
    /// the counter's screen catches up over the same broadcast every mutation sends.
    /// </summary>
    private async void OnRemoveLine(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not LineRow row)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Put this item back?",
            $"{row.Name} comes off your bill. Put it back on the shelf.",
            "Remove",
            "Keep");

        if (!confirmed)
        {
            return;
        }

        var result = await _api.RemoveLineAsync(row.Sequence);

        if (!result.Ok || result.Value?.Cart is null)
        {
            OnTagRejected(new RejectedTag(string.Empty, "error", result.Message ?? "Could not remove that item."));
            return;
        }

        ShowCart(result.Value.Cart);
        RejectBanner.IsVisible = false;
    }

    private async void OnPay(object? sender, EventArgs e)
        => await DisplayAlertAsync("Not built yet", "Paying from the phone is the next piece.", "OK");

    /// <summary>
    /// Pushed rather than replacing this screen, so the live connection behind the basket is never
    /// torn down to look at a receipt. Coming back is an ordinary pop onto a screen that is still
    /// connected and still up to date.
    /// </summary>
    private async void OnPreviousSales(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(PreviousSalesPage));

    private async void OnLeave(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Leave this counter?",
            "You will be signed out and your basket stays with the counter for staff to clear.",
            "Leave",
            "Stay");

        if (!confirmed)
        {
            return;
        }

        await _live.StopAsync();
        await _api.ReleaseTrolleyAsync();

        // Signed out, not merely disconnected. This is a self-checkout unit that the next customer
        // picks up, so leaving the counter has to leave nothing of the last one behind — no token, and
        // no name for the sign-in screen to greet a stranger with.
        SessionStore.Forget();

        // To the start, not back one screen. Popping would land the next customer on the previous
        // customer's sign-in page, still inside their navigation stack.
        await Shell.Current.GoToAsync("//welcome");
    }
}
