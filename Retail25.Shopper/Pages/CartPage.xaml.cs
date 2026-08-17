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

    private string _counterCode = string.Empty;
    private long _cartId;

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
