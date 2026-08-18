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
/// A scanner on a device that has no reader. Returns nothing, and says so.
/// </summary>
public sealed class NullTagScanner : ITagScanner
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<string>> SweepAsync(TimeSpan window, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
