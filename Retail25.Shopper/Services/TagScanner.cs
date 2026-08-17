namespace Retail25.Shopper.Services;

/// <summary>
/// The handheld's UHF reader, as much of it as this app needs: start a sweep, get EPCs, stop.
/// </summary>
public interface ITagScanner
{
    /// <summary>Whether real reader hardware is present and its SDK is loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads for up to <paramref name="window"/> and returns the distinct EPCs seen.</summary>
    Task<IReadOnlyList<string>> SweepAsync(TimeSpan window, CancellationToken ct = default);
}

/// <summary>
/// The Chainway C72's onboard UHF module.
/// <para>
/// Deliberately not implemented yet, and honest about why: driving this module requires Chainway's
/// proprietary <c>DeviceAPI</c> jar (com.rscja.deviceapi), which ships on the vendor CD with the
/// unit and is not downloadable from a public feed. The class exists so the seam is already cut —
/// when the jar lands, an Android binding project wraps it, this class calls
/// <c>RFIDWithUHFUART.getInstance()</c> / <c>startInventoryTag()</c> / <c>readTagFromBuffer()</c>,
/// and nothing above this file changes.
/// </para>
/// <para>
/// Until then <see cref="IsAvailable"/> is false and the cart screen falls back to typed entry,
/// which drives the identical server path — the difference is only who reads the number off the tag.
/// </para>
/// </summary>
public sealed class ChainwayTagScanner : ITagScanner
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<string>> SweepAsync(TimeSpan window, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
