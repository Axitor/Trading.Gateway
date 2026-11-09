namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Tracks and enforces exchange rate limits to prevent bans.</summary>
internal interface IRateLimiter
{
    /// <summary>Charges weight to every window the bucket names, or to none of them.</summary>
    public bool TryReserve(int bucketRaw, int weight, out long waitMillis);

    /// <summary>Performs a lock-free pre-check to verify if an adjustment is needed, then raises the matching window weight to the exchange-reported value without lowering it.</summary>
    public void UpdateWeights(int bucketRaw, int weight);
}
