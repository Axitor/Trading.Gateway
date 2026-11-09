using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;

namespace Axitor.Trading.Gateway.Binance.RateLimiter;

/// <summary>Tracks and enforces exchange rate limits to prevent IP and account bans.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: One lock covers check and charge across all windows. A request is charged to several windows at once,
/// and a lock-free check lets a burst pass before any charge lands (ADR-0007).
/// </para>
/// <para>
/// <b>Connection windows:</b> Stream connection windows sit under the same lock and count attempts, not weight: each attempt charges one.
/// </para>
/// <para>
/// <b>Depth snapshot windows:</b> Our own caps inside the futures and spot request weight windows, not exchange limits.
/// A snapshot is charged to its market's weight window and its snapshot window at once, so resyncs take at most their share and the rest stays free for orders and cancels.
/// The exchange never reports them, so they are never raised from headers.
/// </para>
/// </remarks>
internal sealed class BinanceRateLimiter : IRateLimiter
{
    // .NET 9+ System.Threading.Lock. Avoids legacy 'object' monitor overhead and utilizes zero-allocation ref-struct scopes on the hot path.
    private readonly Lock _sync = new();

    private readonly BinanceRateLimitWindow _futuresRequestWeight;
    private readonly BinanceRateLimitWindow _futuresOrderMinute;
    private readonly BinanceRateLimitWindow _futuresOrderTenSeconds;

    private readonly BinanceRateLimitWindow _spotRequestWeight;
    private readonly BinanceRateLimitWindow _spotOrderTenSeconds;
    private readonly BinanceRateLimitWindow _spotOrderDay;

    private readonly BinanceRateLimitWindow _futuresWsApiRequestWeight;
    private readonly BinanceRateLimitWindow _spotWsApiRequestWeight;
    private readonly bool _futuresWsApiSharesRestWeight;
    private readonly bool _spotWsApiSharesRestWeight;

    private readonly BinanceRateLimitWindow _futuresStreamConnectionsPerFiveMinutes;
    private readonly BinanceRateLimitWindow _spotStreamConnectionsPerFiveMinutes;

    private readonly BinanceRateLimitWindow _futuresDepthSnapshotWeight;
    private readonly BinanceRateLimitWindow _spotDepthSnapshotWeight;

    internal BinanceRateLimiter(BinanceLimits limits)
    {
        _futuresRequestWeight = new BinanceRateLimitWindow(limits.FuturesRestApiLimitRequestWeightPerMinute, SysTime.MillisPerMinute());
        _futuresOrderMinute = new BinanceRateLimitWindow(limits.FuturesRestApiLimitOrderWeightPerMinute, SysTime.MillisPerMinute());
        _futuresOrderTenSeconds = new BinanceRateLimitWindow(limits.FuturesRestApiLimitOrderWeightPer10s, SysTime.MillisPerTenSeconds());

        _spotRequestWeight = new BinanceRateLimitWindow(limits.SpotRestApiLimitRequestWeightPerMinute, SysTime.MillisPerMinute());
        _spotOrderTenSeconds = new BinanceRateLimitWindow(limits.SpotRestApiLimitOrderAmountPer10s, SysTime.MillisPerTenSeconds());
        _spotOrderDay = new BinanceRateLimitWindow(limits.SpotRestApiLimitOrderAmountPer24h, SysTime.MillisPerDay());

        _futuresWsApiRequestWeight = new BinanceRateLimitWindow(limits.FuturesWsApiLimitRequestWeightPerMinute, SysTime.MillisPerMinute());
        _spotWsApiRequestWeight = new BinanceRateLimitWindow(limits.SpotWsApiLimitRequestWeightPerMinute, SysTime.MillisPerMinute());
        _futuresWsApiSharesRestWeight = limits.FuturesWsApiSharesRestRequestWeight;
        _spotWsApiSharesRestWeight = limits.SpotWsApiSharesRestRequestWeight;

        _futuresStreamConnectionsPerFiveMinutes = new BinanceRateLimitWindow(limits.FuturesWebsocketStreamLimitConnectionsAmountPerAttemptInFiveMinutes, SysTime.MillisPerFiveMinutes());
        _spotStreamConnectionsPerFiveMinutes = new BinanceRateLimitWindow(limits.SpotWebsocketStreamLimitConnectionsAmountPerAttemptInFiveMinutes, SysTime.MillisPerFiveMinutes());

        _futuresDepthSnapshotWeight = new BinanceRateLimitWindow(
            limits.FuturesRestApiLimitRequestWeightPerMinute * limits.FuturesDepthSnapshotWeightPercent / 100,
            SysTime.MillisPerMinute());

        _spotDepthSnapshotWeight = new BinanceRateLimitWindow(
            limits.SpotRestApiLimitRequestWeightPerMinute * limits.SpotDepthSnapshotWeightPercent / 100,
            SysTime.MillisPerMinute());
    }

    /// <summary>Charges weight to every window the bucket names, or to none of them. Connection windows charge one attempt.</summary>
    public bool TryReserve(int bucketRaw, int weight, out long waitMillis)
    {
        long nowMillis = SysTime.UtcMillis();
        BinanceRateLimitBucket bucket = Resolve((BinanceRateLimitBucket)bucketRaw);

        bool futuresRequestWeight = (bucket & BinanceRateLimitBucket.FuturesRequestWeight) != 0;
        bool futuresOrderMinute = (bucket & BinanceRateLimitBucket.FuturesOrderMinute) != 0;
        bool futuresOrderTenSeconds = (bucket & BinanceRateLimitBucket.FuturesOrderTenSeconds) != 0;
        bool spotRequestWeight = (bucket & BinanceRateLimitBucket.SpotRequestWeight) != 0;
        bool spotOrderTenSeconds = (bucket & BinanceRateLimitBucket.SpotOrderTenSeconds) != 0;
        bool spotOrderDay = (bucket & BinanceRateLimitBucket.SpotOrderDay) != 0;
        bool futuresWsApiRequestWeight = (bucket & BinanceRateLimitBucket.FuturesWsApiRequestWeight) != 0;
        bool spotWsApiRequestWeight = (bucket & BinanceRateLimitBucket.SpotWsApiRequestWeight) != 0;
        bool futuresStreamConnections = (bucket & BinanceRateLimitBucket.FuturesStreamConnections) != 0;
        bool spotStreamConnections = (bucket & BinanceRateLimitBucket.SpotStreamConnections) != 0;
        bool futuresDepthSnapshotWeight = (bucket & BinanceRateLimitBucket.FuturesDepthSnapshotWeight) != 0;
        bool spotDepthSnapshotWeight = (bucket & BinanceRateLimitBucket.SpotDepthSnapshotWeight) != 0;

        lock (_sync)
        {
            if (futuresRequestWeight && !_futuresRequestWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (futuresOrderMinute && !_futuresOrderMinute.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (futuresOrderTenSeconds && !_futuresOrderTenSeconds.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (spotRequestWeight && !_spotRequestWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (spotOrderTenSeconds && !_spotOrderTenSeconds.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (spotOrderDay && !_spotOrderDay.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (futuresWsApiRequestWeight && !_futuresWsApiRequestWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (spotWsApiRequestWeight && !_spotWsApiRequestWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (futuresStreamConnections && !_futuresStreamConnectionsPerFiveMinutes.CanCharge(nowMillis, 1, out waitMillis)) return false;
            if (spotStreamConnections && !_spotStreamConnectionsPerFiveMinutes.CanCharge(nowMillis, 1, out waitMillis)) return false;
            if (futuresDepthSnapshotWeight && !_futuresDepthSnapshotWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;
            if (spotDepthSnapshotWeight && !_spotDepthSnapshotWeight.CanCharge(nowMillis, weight, out waitMillis)) return false;

            if (futuresRequestWeight) _futuresRequestWeight.Charge(nowMillis, weight);
            if (futuresOrderMinute) _futuresOrderMinute.Charge(nowMillis, weight);
            if (futuresOrderTenSeconds) _futuresOrderTenSeconds.Charge(nowMillis, weight);
            if (spotRequestWeight) _spotRequestWeight.Charge(nowMillis, weight);
            if (spotOrderTenSeconds) _spotOrderTenSeconds.Charge(nowMillis, weight);
            if (spotOrderDay) _spotOrderDay.Charge(nowMillis, weight);
            if (futuresWsApiRequestWeight) _futuresWsApiRequestWeight.Charge(nowMillis, weight);
            if (spotWsApiRequestWeight) _spotWsApiRequestWeight.Charge(nowMillis, weight);
            if (futuresStreamConnections) _futuresStreamConnectionsPerFiveMinutes.Charge(nowMillis, 1);
            if (spotStreamConnections) _spotStreamConnectionsPerFiveMinutes.Charge(nowMillis, 1);
            if (futuresDepthSnapshotWeight) _futuresDepthSnapshotWeight.Charge(nowMillis, weight);
            if (spotDepthSnapshotWeight) _spotDepthSnapshotWeight.Charge(nowMillis, weight);

            waitMillis = 0;
            return true;
        }
    }

    /// <summary>Performs a lock-free pre-check to verify if an adjustment is needed, then raises the matching window weight to the exchange-reported value without lowering it.</summary>
    public void UpdateWeights(int bucketRaw, int weight)
    {
        long nowMillis = SysTime.UtcMillis();
        BinanceRateLimitBucket bucket = Resolve((BinanceRateLimitBucket)bucketRaw);

        BinanceRateLimitWindow? window = bucket switch
        {
            BinanceRateLimitBucket.FuturesRequestWeight => _futuresRequestWeight,
            BinanceRateLimitBucket.FuturesOrderMinute => _futuresOrderMinute,
            BinanceRateLimitBucket.FuturesOrderTenSeconds => _futuresOrderTenSeconds,
            BinanceRateLimitBucket.SpotRequestWeight => _spotRequestWeight,
            BinanceRateLimitBucket.SpotOrderTenSeconds => _spotOrderTenSeconds,
            BinanceRateLimitBucket.SpotOrderDay => _spotOrderDay,
            BinanceRateLimitBucket.FuturesWsApiRequestWeight => _futuresWsApiRequestWeight,
            BinanceRateLimitBucket.SpotWsApiRequestWeight => _spotWsApiRequestWeight,
            _ => null
        };

        if (window is null || !window.NeedsRaise(nowMillis, weight)) return;

        lock (_sync)
        {
            window.Raise(nowMillis, weight);
        }
    }

    /// <summary>Moves WebSocket API weight to the REST window when the limits are shared.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private BinanceRateLimitBucket Resolve(BinanceRateLimitBucket bucket)
    {
        if (_futuresWsApiSharesRestWeight && (bucket & BinanceRateLimitBucket.FuturesWsApiRequestWeight) != 0)
        {
            bucket = (bucket & ~BinanceRateLimitBucket.FuturesWsApiRequestWeight) | BinanceRateLimitBucket.FuturesRequestWeight;
        }

        if (_spotWsApiSharesRestWeight && (bucket & BinanceRateLimitBucket.SpotWsApiRequestWeight) != 0)
        {
            bucket = (bucket & ~BinanceRateLimitBucket.SpotWsApiRequestWeight) | BinanceRateLimitBucket.SpotRequestWeight;
        }

        return bucket;
    }
}
