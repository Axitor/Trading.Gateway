using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.RateLimiter;

/// <summary>Struct wrapper over the limiter, so generic callers get their own specialized code and direct calls.</summary>
/// <remarks>
/// DESIGN NOTE: A class type argument shares one copy of the code and calls through the interface; a struct gets its own copy (ADR-0005).
/// </remarks>
internal readonly struct BinanceRateLimiterRef : IRateLimiter
{
    private readonly BinanceRateLimiter _inner;

    public BinanceRateLimiterRef(BinanceRateLimiter inner) => _inner = inner;

    /// <summary>Charges weight to every window the bucket names, or to none of them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReserve(int bucketRaw, int weight, out long waitMillis) => _inner.TryReserve(bucketRaw, weight, out waitMillis);

    /// <summary>Raises the matching windows to the exchange's count, never lowers them; takes no lock when nothing changes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateWeights(int bucketRaw, int weight) => _inner.UpdateWeights(bucketRaw, weight);
}
