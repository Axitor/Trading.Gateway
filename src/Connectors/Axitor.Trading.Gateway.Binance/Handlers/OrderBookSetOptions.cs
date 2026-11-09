namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>Settings of an OrderBookSet.</summary>
public sealed record OrderBookSetOptions
{
    /// <summary>
    /// Snapshot levels per side. Futures take 5, 10, 20, 50, 100, 500 or 1000: weight 2 up to 50 levels, then 5, 10 and 20.
    /// Spot takes 1 to 5000: weight 5 up to 100 levels, 25 up to 500, 50 up to 1000, 250 above.
    /// </summary>
    public int SnapshotLimit { get; init; } = 1000;

    /// <summary>Known levels per side below which a book loads a fresh snapshot while it keeps serving. Zero takes a quarter of SnapshotLimit.</summary>
    public int RefreshThreshold { get; init; }

    /// <summary>Most bytes of updates a book buffers while its snapshot loads. Past it the buffer starts over.</summary>
    public int MaxBufferBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>First delay before a failed snapshot is tried again.</summary>
    public TimeSpan SnapshotRetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Longest delay between snapshot tries.</summary>
    public TimeSpan SnapshotRetryMaxDelay { get; init; } = TimeSpan.FromSeconds(30);
}
