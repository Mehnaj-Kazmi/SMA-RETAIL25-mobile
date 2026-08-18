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

/// <summary>
/// The sound a scan makes. Two outcomes, two noises — see the Android implementation for why one
/// would be useless.
/// </summary>
public interface IScanFeedback
{
    /// <summary>Items went on the bill.</summary>
    void Accepted();

    /// <summary>The shop refused the tag, or there was nothing in range.</summary>
    void Refused();
}

/// <summary>Silence, for a platform with no tone generator.</summary>
public sealed class NullScanFeedback : IScanFeedback
{
    public void Accepted()
    {
    }

    public void Refused()
    {
    }
}
