using System.Collections.ObjectModel;
using System.Globalization;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Pages;

/// <summary>One past visit as the list renders it, with the formatting already done.</summary>
public sealed class SaleRow
{
    public required string Heading { get; init; }

    public required string Detail { get; init; }

    public required string Amount { get; init; }
}

/// <summary>
/// What this customer has bought here before.
/// <para>
/// One of the two screens a signed-in customer may reach — the basket and this. Everything the shop's
/// staff use is a different application with a different token, and a shopper's token carries no
/// permissions at all, so that is a property of the server rather than a menu this app declines to
/// draw.
/// </para>
/// <para>
/// Plain HTTP, deliberately, where the basket uses a live connection. History does not change while
/// it is being read, so a socket would be a connection held open for nothing.
/// </para>
/// </summary>
public partial class PreviousSalesPage : ContentPage
{
    private readonly ObservableCollection<SaleRow> _rows = [];
    private readonly ShopperApi _api = new();

    private bool _loaded;

    public PreviousSalesPage()
    {
        InitializeComponent();

        SalesView.ItemsSource = _rows;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Once. Coming back from anywhere would otherwise re-fetch a list that cannot have changed
        // while this screen was the one in front of the customer.
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        var result = await _api.GetPreviousSalesAsync();

        if (!result.Ok || result.Value is null)
        {
            SummaryLabel.Text = "Unavailable";
            EmptyTitle.Text = "Could not load your purchases";
            EmptyBody.Text = result.Message ?? "Check your connection and try again.";
            return;
        }

        _rows.Clear();

        foreach (var sale in result.Value)
        {
            _rows.Add(new SaleRow
            {
                Heading = $"Receipt {sale.TransactionNumber.ToString(CultureInfo.InvariantCulture)}",

                // Local time, because a receipt is remembered as "Tuesday afternoon" and the server
                // hands these over in UTC.
                Detail = $"{sale.CompletedAt.ToLocalTime():d MMM yyyy, HH:mm}  ·  "
                    + $"{sale.ItemCount} item{(sale.ItemCount == 1 ? string.Empty : "s")}  ·  "
                    + $"counter {sale.CounterCode}",

                Amount = sale.Total.ToString("N2", CultureInfo.InvariantCulture),
            });
        }

        SummaryLabel.Text = _rows.Count switch
        {
            0 => "Nothing yet",
            1 => "1 visit",
            _ => $"{_rows.Count} visits",
        };
    }

    private async void OnBack(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..");
}
