using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.RateLimiter;

namespace Axitor.Trading.Gateway.Tests.Binance.Limits;

/// <summary>Rate limit windows: a request is charged to every window it names or to none, so an IP ban never comes from our own count.</summary>
public sealed class RateLimiterTests
{
    private const int FuturesRequestWeight = (int)BinanceRateLimitBucket.FuturesRequestWeight;
    private const int FuturesOrderMinute = (int)BinanceRateLimitBucket.FuturesOrderMinute;
    private const int FuturesOrder = (int)BinanceRateLimitBucket.FuturesOrder;
    private const int FuturesSnapshot = (int)(BinanceRateLimitBucket.FuturesRequestWeight | BinanceRateLimitBucket.FuturesDepthSnapshotWeight);
    private const int SpotRequestWeight = (int)BinanceRateLimitBucket.SpotRequestWeight;
    private const int SpotWsApiRequestWeight = (int)BinanceRateLimitBucket.SpotWsApiRequestWeight;
    private const int FuturesStreamConnections = (int)BinanceRateLimitBucket.FuturesStreamConnections;

    [Fact]
    public void ChargesEveryWindowOrNone()
    {
        BinanceRateLimiter limiter = new(new BinanceLimits()
        {
            FuturesRestApiLimitOrderWeightPer10s = 3,
            FuturesRestApiLimitOrderWeightPerMinute = 4,
        });

        Assert.True(limiter.TryReserve(FuturesOrder, 1, out _));
        Assert.True(limiter.TryReserve(FuturesOrder, 1, out _));
        Assert.True(limiter.TryReserve(FuturesOrder, 1, out _));

        // The 10s window is full: the order waits, and the minute window is not charged for it.
        Assert.False(limiter.TryReserve(FuturesOrder, 1, out long waitMillis));
        Assert.True(0 < waitMillis);

        Assert.True(limiter.TryReserve(FuturesOrderMinute, 1, out _));
        Assert.False(limiter.TryReserve(FuturesOrderMinute, 1, out _));
    }

    [Fact]
    public void ReportedUsageRaisesButNeverLowers()
    {
        BinanceRateLimiter limiter = new(new BinanceLimits() { FuturesRestApiLimitRequestWeightPerMinute = 10 });

        limiter.UpdateWeights(FuturesRequestWeight, 8);

        Assert.False(limiter.TryReserve(FuturesRequestWeight, 3, out _));
        Assert.True(limiter.TryReserve(FuturesRequestWeight, 2, out _));

        // A lower report arrives late: our own count stays.
        limiter.UpdateWeights(FuturesRequestWeight, 5);

        Assert.False(limiter.TryReserve(FuturesRequestWeight, 1, out _));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WsApiWeightGoesToRestWindowWhenShared(bool isShared, bool restStillFits)
    {
        BinanceRateLimiter limiter = new(new BinanceLimits()
        {
            SpotRestApiLimitRequestWeightPerMinute = 10,
            SpotWsApiLimitRequestWeightPerMinute = 1000,
            SpotWsApiSharesRestRequestWeight = isShared,
        });

        Assert.True(limiter.TryReserve(SpotWsApiRequestWeight, 10, out _));
        Assert.Equal(restStillFits, limiter.TryReserve(SpotRequestWeight, 1, out _));
    }

    [Fact]
    public void SnapshotsTakeOnlyTheirShare()
    {
        BinanceRateLimiter limiter = new(new BinanceLimits()
        {
            FuturesRestApiLimitRequestWeightPerMinute = 100,
            FuturesDepthSnapshotWeightPercent = 20,
        });

        Assert.True(limiter.TryReserve(FuturesSnapshot, 20, out _));
        Assert.False(limiter.TryReserve(FuturesSnapshot, 1, out _));

        // The rest of the weight stays free for orders and cancels.
        Assert.True(limiter.TryReserve(FuturesRequestWeight, 80, out _));
        Assert.False(limiter.TryReserve(FuturesRequestWeight, 1, out _));
    }

    [Fact]
    public void ConnectionWindowCountsAttemptsNotWeight()
    {
        BinanceRateLimiter limiter = new(new BinanceLimits() { FuturesWebsocketStreamLimitConnectionsAmountPerAttemptInFiveMinutes = 2 });

        Assert.True(limiter.TryReserve(FuturesStreamConnections, 100, out _));
        Assert.True(limiter.TryReserve(FuturesStreamConnections, 100, out _));
        Assert.False(limiter.TryReserve(FuturesStreamConnections, 1, out _));
    }
}
