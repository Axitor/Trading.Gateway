using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Binance.Sinks;

/// <summary>Latest value per subscription: a strategy reading it never acts on a stale price.</summary>
public sealed class LatestCacheTests
{
    [Fact]
    public void NothingToReadBeforeFirstMessage()
    {
        LatestCache<BookTickerModel> cache = new(4);

        Assert.False(cache.TryRead(0, out _, out long version));
        Assert.False(cache.TryReadLast(0, out _, out _));
        Assert.Equal(0, version);
        Assert.Equal(0, cache.GetVersion(0));
    }

    [Fact]
    public void ReadsLatestMessageWithItsVersion()
    {
        LatestCache<BookTickerModel> cache = new(4);

        cache.Handler.OnData(0, Ticker(1));
        cache.Handler.OnData(0, Ticker(2));

        Assert.True(cache.TryRead(0, out BookTickerModel model, out long version));
        Assert.Equal(2, model.UpdateId);
        Assert.Equal(2, version);
        Assert.Equal(2, cache.GetVersion(0));

        // Subscriptions do not share slots.
        Assert.False(cache.TryRead(1, out _, out _));
    }

    [Fact]
    public void BreakMakesValueStaleUntilNewMessage()
    {
        LatestCache<BookTickerModel> cache = new(4);
        cache.Handler.OnData(0, Ticker(1));

        cache.Handler.OnDisconnected(0, WsDisconnectReason.ExceptionSocket, isFatal: false);

        Assert.False(cache.TryRead(0, out _, out _));
        Assert.True(cache.TryReadLast(0, out BookTickerModel last, out _));
        Assert.Equal(1, last.UpdateId);

        // Data flows again, but this value is still from before the break.
        cache.Handler.OnReconnected(0, new WsStreamGap());
        Assert.False(cache.TryRead(0, out _, out _));

        cache.Handler.OnData(0, Ticker(5));
        Assert.True(cache.TryRead(0, out BookTickerModel fresh, out _));
        Assert.Equal(5, fresh.UpdateId);
    }

    [Fact]
    public void UnsubscribeClearsSlot()
    {
        LatestCache<BookTickerModel> cache = new(4);
        cache.Handler.OnData(0, Ticker(1));

        // The handle may go to another stream: its old value must not be read as the new one.
        cache.Handler.OnUnsubscribed(0);

        Assert.False(cache.TryRead(0, out _, out _));
        Assert.False(cache.TryReadLast(0, out _, out _));
    }

    private static BookTickerModel Ticker(long updateId) => new() { UpdateId = updateId, BidPrice = 100, AskPrice = 101 };
}
