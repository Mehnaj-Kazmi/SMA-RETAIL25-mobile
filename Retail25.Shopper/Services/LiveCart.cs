using Microsoft.AspNetCore.SignalR.Client;

namespace Retail25.Shopper.Services;

/// <summary>A tag the reader saw but the system could not turn into a sale line.</summary>
public sealed record RejectedTag(string? Epc, string? Reason, string? Message);

/// <summary>
/// The live connection to the counter, over a WebSocket.
/// <para>
/// This is what makes the basket update by itself. The alternative — asking the server "anything
/// new?" on a timer — needs a request every second or so to feel immediate, which on twenty phones
/// is twenty requests a second almost all answering "nothing", and an item still appears up to a
/// second late. Here the server already knows the moment a tag is read, and pushes it down a
/// connection that is already open.
/// </para>
/// <para>
/// SignalR rather than a hand-rolled socket. It negotiates a real WebSocket — the transport is
/// exactly what was asked for — and brings the parts that are tedious and easy to get wrong:
/// reconnect with backoff, keepalive so a dead connection is noticed rather than silently ignored,
/// and message ordering. A shopper walks behind a freezer and loses wi-fi for three seconds; that
/// path has to work, and it is not where hand-written code earns its keep.
/// </para>
/// </summary>
public sealed class LiveCart : IAsyncDisposable
{
    private readonly ShopperApi _api;

    private HubConnection? _connection;
    private long _cartId;

    public LiveCart(ShopperApi api) => _api = api;

    /// <summary>Items just added by the reader.</summary>
    public event Action<IReadOnlyList<CartLine>>? LinesAdded;

    /// <summary>The money changed. Always from the server; never computed here.</summary>
    public event Action<CartTotals>? TotalsChanged;

    /// <summary>The whole basket, after an edit the server considered wholesale.</summary>
    public event Action<Cart>? CartReplaced;

    /// <summary>A tag was seen and refused, with the reason why.</summary>
    public event Action<RejectedTag>? TagRejected;

    /// <summary>Connected, reconnecting, or gone — for the status strip.</summary>
    public event Action<string>? StateChanged;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task<bool> StartAsync(long cartId, CancellationToken ct = default)
    {
        _cartId = cartId;

        await StopAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl(
                $"{ApiSettings.BaseUrl.TrimEnd('/')}/hubs/pos",
                options =>
                {
                    // Fetched per connection attempt, not once.
                    //
                    // A hub ticket is single-use and lives sixty seconds, so the value that opened
                    // the first connection is already spent by the time a reconnect happens. Handing
                    // SignalR a factory means every attempt — including automatic ones after a
                    // dropout — collects a fresh one. A captured ticket buys nothing.
                    options.AccessTokenProvider = async () =>
                    {
                        var issued = await _api.GetHubTicketAsync();
                        return issued.Ok ? issued.Value?.Ticket : null;
                    };
                })

            // 0s, 2s, 10s, 30s, then keep trying every 30s. A shop has dead spots; giving up on the
            // fourth attempt would strand a shopper mid-aisle with a basket that has stopped moving.
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
            ])
            .Build();

        Subscribe(_connection);

        try
        {
            await _connection.StartAsync(ct);
            await _connection.InvokeAsync("JoinCart", _cartId, ct);
            Raise(StateChanged, "connected");
            return true;
        }
        catch (Exception)
        {
            // Server down, wrong address, ticket refused. The page falls back to what it loaded over
            // HTTP and shows a disconnected strip rather than an empty screen.
            Raise(StateChanged, "offline");
            return false;
        }
    }

    private void Subscribe(HubConnection connection)
    {
        connection.On<IReadOnlyList<CartLine>, int>(
            "CartLinesAdded",
            (lines, _) => Raise(LinesAdded, lines));

        connection.On<CartTotals, int>(
            "TotalsChanged",
            (totals, _) => Raise(TotalsChanged, totals));

        connection.On<Cart, int>(
            "CartUpdated",
            (cart, _) => Raise(CartReplaced, cart));

        connection.On<RejectedTag>(
            "CartLineRejected",
            rejected => Raise(TagRejected, rejected));

        connection.Reconnecting += _ =>
        {
            Raise(StateChanged, "reconnecting");
            return Task.CompletedTask;
        };

        // Group membership does not survive a reconnect: it is state on the server side of a
        // connection that no longer exists. Re-joining here is what stops the basket going quiet
        // after a dropout while the connection itself looks perfectly healthy.
        connection.Reconnected += async _ =>
        {
            try
            {
                await connection.InvokeAsync("JoinCart", _cartId);
                Raise(StateChanged, "connected");
            }
            catch (Exception)
            {
                Raise(StateChanged, "offline");
            }
        };

        connection.Closed += _ =>
        {
            Raise(StateChanged, "offline");
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Hub callbacks arrive on a background thread; touching a control from one crashes on Android.
    /// Marshalling here rather than in each page means no subscriber can forget.
    /// </summary>
    private static void Raise<T>(Action<T>? handler, T payload)
    {
        if (handler is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => handler(payload));
    }

    public async Task StopAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception)
        {
            // Already torn down. Nothing to salvage and nothing worth telling the shopper.
        }

        _connection = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
