using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Retail25.Shopper.Services;

public sealed record ShopperInfo(
    long Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    bool EmailConfirmed);

public sealed record ShopperSession(
    ShopperInfo Shopper,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    bool BiometricEnabled);

/// <summary>One item in the basket, as the till sees it.</summary>
public sealed record CartLine(
    int Sequence,
    string Name,
    string? VariantLabel,
    string? Epc,
    decimal Quantity,
    decimal UnitPrice,
    decimal ExtendedNet);

/// <summary>
/// The money. Taken from the server untouched â€” the app never adds anything up itself, because a
/// total computed twice is a total that can disagree with the receipt.
/// </summary>
public sealed record CartTotals(
    decimal Subtotal,
    string Tax1Name,
    decimal Tax1Total,
    string Tax2Name,
    decimal Tax2Total,
    decimal GrandTotal,
    int ItemCount)
{
    public static readonly CartTotals Empty = new(0, "Tax", 0, "Tax 2", 0, 0, 0);
}

public sealed record Cart(
    long Id,
    int Revision,
    IReadOnlyList<CartLine> Lines,
    CartTotals Totals);

/// <summary>The claimed counter and what is currently in its basket.</summary>
public sealed record TrolleyClaim(
    long SessionId,
    long TrolleyId,
    string TrolleyCode,
    string State,
    Cart? Cart);

/// <summary>Credentials for the live connection. Single-use, sixty seconds.</summary>
public sealed record ShopperHubTicket(string Ticket, int ExpiresInSeconds, long CartId);

/// <summary>One past visit, as the previous-sales screen lists it.</summary>
public sealed record ShopperSale(
    long SaleId,
    long TransactionNumber,
    DateTimeOffset CompletedAt,
    decimal Total,
    int ItemCount,
    string CounterCode);

/// <summary>
/// Success with a value, or a failure carrying something worth showing a shopper.
/// <para>
/// Exceptions are caught at the edge and turned into one of these rather than thrown onward. A phone
/// in a shop loses signal constantly, and "the server is not reachable" is an ordinary outcome of
/// pressing a button here, not an exceptional one.
/// </para>
/// </summary>
public sealed record ApiResult<T>(bool Ok, T? Value, string? Message, string? Code)
{
    public static ApiResult<T> Success(T value) => new(true, value, null, null);

    public static ApiResult<T> Failure(string message, string? code = null) => new(false, default, message, code);
}

/// <summary>
/// Where the API lives.
/// <para>
/// Overridable at runtime through <see cref="Preferences"/> so a phone can be pointed at a different
/// machine without a rebuild â€” on a shop network the server's address is a deployment detail, and
/// baking it into the binary means a new binary every time somebody's laptop gets a new lease.
/// </para>
/// </summary>
public static class ApiSettings
{
    private const string Key = "api.baseUrl";

    /// <summary>
    /// The deployed store server. The API is a sub-application at <c>/backend</c>, not the site root:
    /// the root is the till's web front end, so a base URL without that segment reaches Next.js and
    /// every call comes back as HTML.
    /// <para>
    /// HTTPS, and that is load-bearing rather than tidiness. Android blocks cleartext by default, and
    /// the exception this app ships covers private ranges only — a handset on mobile data has no route
    /// to a shop LAN anyway.
    /// </para>
    /// <para>
    /// To point a handset at a laptop instead, override it at runtime through
    /// <see cref="BaseUrl"/> rather than editing this — e.g. <c>http://192.168.18.40:5000</c>. Not
    /// <c>localhost</c>: on a handset that resolves to the handset, which is the single most common
    /// reason a phone app "cannot reach the server" while the same URL works in a browser on the
    /// machine running it.
    /// </para>
    /// </summary>
    public const string DefaultBaseUrl = "https://pos.sma-techno.net/backend";

    public static string BaseUrl
    {
        get => Preferences.Default.Get(Key, DefaultBaseUrl);
        set => Preferences.Default.Set(Key, value);
    }
}

/// <summary>
/// The signed-in session, kept across app restarts.
/// <para>
/// The refresh token goes to <see cref="SecureStorage"/>, which on Android is the Keystore â€” the
/// Keystore. Preferences would put a working credential in a plain
/// XML file readable by anything that gets root on the handset.
/// </para>
/// </summary>
public static class SessionStore
{
    private const string RefreshKey = "shopper.refreshToken";
    private const string DeviceKey = "shopper.deviceId";
    private const string NameKey = "shopper.firstName";

    public static string? AccessToken { get; private set; }

    public static DateTimeOffset AccessTokenExpiresAt { get; private set; }

    public static ShopperInfo? Shopper { get; private set; }

    public static string FirstName => Preferences.Default.Get(NameKey, string.Empty);

    /// <summary>
    /// A per-installation id, made once and kept. Not a hardware identifier â€” Android stopped handing
    /// those out, and "reinstalling forgets this phone" is the right granularity anyway.
    /// </summary>
    public static string DeviceId
    {
        get
        {
            var existing = Preferences.Default.Get(DeviceKey, string.Empty);

            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            var created = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(DeviceKey, created);
            return created;
        }
    }

    public static string DeviceName => DeviceInfo.Current.Model is { Length: > 0 } model
        ? model
        : "Phone";

    public static async Task AdoptAsync(ShopperSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        AccessToken = session.AccessToken;
        AccessTokenExpiresAt = session.ExpiresAt;
        Shopper = session.Shopper;

        Preferences.Default.Set(NameKey, session.Shopper.FirstName);

        await SecureStorage.Default.SetAsync(RefreshKey, session.RefreshToken);
    }

    public static async Task<string?> ReadRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(RefreshKey);
        }
        catch (Exception)
        {
            // A Keystore entry can be invalidated by the OS â€” a restore to a new handset, or the
            // screen lock being removed. Unreadable is the same as absent: sign in again.
            return null;
        }
    }

    /// <summary>
    /// Hands the handset back: everything the last customer left behind goes, not just their tokens.
    /// <para>
    /// The stored first name is removed here and that is the point of this method existing in the
    /// shape it does. On a customer's own phone, greeting them by name on the sign-in screen is
    /// friendly and the name is their own. On a self-checkout handset passed from shopper to shopper
    /// it is somebody else's name shown to a stranger — the next customer picks the unit up and is
    /// told "WELCOME BACK, SARA". Clearing the token while leaving the name behind fixes the part
    /// nobody can see and leaves the part everybody can.
    /// </para>
    /// </summary>
    public static void Forget()
    {
        AccessToken = null;
        AccessTokenExpiresAt = default;
        Shopper = null;

        Preferences.Default.Remove(NameKey);
        SecureStorage.Default.Remove(RefreshKey);
    }
}

/// <summary>The phone's whole conversation with the server.</summary>
public sealed class ShopperApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ShopperApi()
    {
        _http = new HttpClient
        {
            // The trailing slash is load-bearing, and its absence is silent.
            //
            // Uri treats the last segment of a base address as a file unless it ends in a slash, and
            // replaces it when a relative path is combined. So a base of
            // "https://pos.sma-techno.net/backend" plus "api/v1/shopper/account/sign-in" resolves to
            // "https://pos.sma-techno.net/api/v1/..." — the /backend sub-application dropped. That URL
            // is served by the front end, which answers 404, and the app reports "the server does not
            // have that endpoint", which sends you looking at the server for a fault that is here.
            //
            // It cost nothing while the API was a bare origin on a laptop, because there was no path
            // segment to lose. Moving to a hosted sub-application is what made it break, and the
            // normalisation is done here rather than in the constant so a base URL typed into
            // Preferences at runtime cannot reintroduce it.
            BaseAddress = new Uri(ApiSettings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),

            // Short. A shopper standing at a trolley will decide the app is broken long before a
            // default hundred-second timeout expires, and would rather be told to try again.
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public Task<ApiResult<ShopperSession>> RegisterAsync(
        string firstName,
        string lastName,
        string phone,
        string email,
        string password,
        CancellationToken ct = default)
        => PostAsync<ShopperSession>(
            "api/v1/shopper/account/register",
            new
            {
                firstName,
                lastName,
                phone,
                email,
                password,
                deviceId = SessionStore.DeviceId,
                deviceName = SessionStore.DeviceName,
            },
            ct);

    public Task<ApiResult<ShopperSession>> SignInAsync(
        string email,
        string password,
        CancellationToken ct = default)
        => PostAsync<ShopperSession>(
            "api/v1/shopper/account/sign-in",
            new
            {
                email,
                password,
                deviceId = SessionStore.DeviceId,
                deviceName = SessionStore.DeviceName,
            },
            ct);

    public Task<ApiResult<ShopperSession>> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => PostAsync<ShopperSession>(
            "api/v1/shopper/account/refresh",
            new { refreshToken, deviceName = SessionStore.DeviceName },
            ct);

    /// <summary>
    /// Asks the shop for a self-checkout counter and opens the basket on it. Called straight after
    /// signing in, and again on every cold start — the customer never types a number.
    /// <para>
    /// Safe to repeat. A shopper already mid-trip is given that same trip back rather than a second
    /// counter, so this doubles as "where was I?" without a separate call to ask first.
    /// </para>
    /// </summary>
    public Task<ApiResult<TrolleyClaim>> StartSelfCheckoutAsync(CancellationToken ct = default)
        => PostAsync<TrolleyClaim>("api/v1/shopper/self-checkout", new { }, ct, authenticated: true);

    /// <summary>
    /// Kept for the counter whose reader is bolted down: where the hardware decides which station a
    /// customer is at, being issued a different one would watch the wrong basket.
    /// </summary>
    public Task<ApiResult<TrolleyClaim>> ClaimTrolleyAsync(string code, CancellationToken ct = default)
        => PostAsync<TrolleyClaim>("api/v1/shopper/trolleys/claim", new { code }, ct, authenticated: true);

    /// <summary>This shopper's own past visits, newest first.</summary>
    public Task<ApiResult<IReadOnlyList<ShopperSale>>> GetPreviousSalesAsync(CancellationToken ct = default)
        => SendAsync<IReadOnlyList<ShopperSale>>(
            HttpMethod.Get,
            "api/v1/shopper/sales?take=20",
            null,
            ct,
            authenticated: true);

    /// <summary>
    /// The basket as it stands. Called on a cold start and after a reconnect â€” the live connection
    /// carries changes, not history, so something has to establish the starting point.
    /// </summary>
    public Task<ApiResult<TrolleyClaim>> GetMyCartAsync(CancellationToken ct = default)
        => SendAsync<TrolleyClaim>(HttpMethod.Get, "api/v1/shopper/cart", null, ct, authenticated: true);

    public Task<ApiResult<ShopperHubTicket>> GetHubTicketAsync(CancellationToken ct = default)
        => PostAsync<ShopperHubTicket>("api/v1/shopper/hub-ticket", new { }, ct, authenticated: true);

    public Task<ApiResult<object>> ReleaseTrolleyAsync(CancellationToken ct = default)
        => PostAsync<object>("api/v1/shopper/trolleys/release", new { }, ct, authenticated: true);

    private Task<ApiResult<T>> PostAsync<T>(
        string path,
        object body,
        CancellationToken ct,
        bool authenticated = false)
        => SendAsync<T>(HttpMethod.Post, path, body, ct, authenticated);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct,
        bool authenticated = false)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            if (authenticated && SessionStore.AccessToken is { Length: > 0 } token)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                // 204, or a success with an empty body. Release answers this way, and asking
                // ReadFromJsonAsync to parse nothing throws rather than returning null.
                if (response.StatusCode == HttpStatusCode.NoContent
                    || response.Content.Headers.ContentLength is null or 0)
                {
                    return new ApiResult<T>(true, default, null, null);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);

                return value is null
                    ? ApiResult<T>.Failure("The server sent back something we could not read.")
                    : ApiResult<T>.Success(value);
            }

            return await ReadProblemAsync<T>(response, ct);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Failure("The server took too long to answer. Check your connection and try again.");
        }
        catch (HttpRequestException)
        {
            // Deliberately not showing the exception text. "No connection could be made because the
            // target machine actively refused it" is a sentence for a developer, not for somebody
            // holding a trolley.
            return ApiResult<T>.Failure($"Cannot reach the store server at {ApiSettings.BaseUrl}.");
        }
    }

    /// <summary>
    /// Unpacks the API's RFC 7807 body, which carries a human-readable <c>detail</c> and a stable
    /// machine <c>code</c>. Falling back to the status line only when the body is not what we expect.
    /// </summary>
    private static async Task<ApiResult<T>> ReadProblemAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;

            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;

            if (!string.IsNullOrWhiteSpace(detail))
            {
                return ApiResult<T>.Failure(detail, code);
            }
        }
        catch (JsonException)
        {
            // Not a problem document â€” fall through to the status line.
        }

        return ApiResult<T>.Failure(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Sign in again to continue.",
            HttpStatusCode.NotFound => "The server does not have that endpoint. Is it running the latest build?",
            _ => $"The server refused the request ({(int)response.StatusCode}).",
        });
    }
}

