using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Dispatch;

/// <summary>Dispatchers: while a rotation overlaps, both connections deliver, and the handler must see each message once.</summary>
public sealed class DispatcherTests
{
    private const int Capacity = 4;

    [Fact]
    public void DropsRepeatedAndOlderUpdates()
    {
        StreamLog<BookTickerModel> log = new();
        BinanceStreamDispatcher<FuturesBookTickerParser, BookTickerModel, StreamLogHandler<BookTickerModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, Ticker(10)));
        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, Ticker(10)));
        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, Ticker(9)));
        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, Ticker(11)));

        Assert.Equal([10L, 11L], UpdateIds(log));
    }

    [Fact]
    public void EachSubscriptionCountsOnItsOwnAndRestartsOnReuse()
    {
        StreamLog<BookTickerModel> log = new();
        BinanceStreamDispatcher<FuturesBookTickerParser, BookTickerModel, StreamLogHandler<BookTickerModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);
        dispatcher.OnSubscribing(1);

        dispatcher.Dispatch(0, Ticker(10));
        dispatcher.Dispatch(1, Ticker(5));

        // The handle goes to another stream: its ids start over.
        dispatcher.OnSubscribing(0);
        dispatcher.Dispatch(0, Ticker(3));

        Assert.Equal([10L, 5L, 3L], UpdateIds(log));
        Assert.Equal([0, 1, 0], log.Data.Select(static record => record.Subscription).ToArray());
    }

    [Fact]
    public void UnreadableDataIsReportedNotDelivered()
    {
        StreamLog<BookTickerModel> log = new();
        BinanceStreamDispatcher<FuturesBookTickerParser, BookTickerModel, StreamLogHandler<BookTickerModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        Assert.Equal(WsDispatchResult.Unreadable, dispatcher.Dispatch(0, """{"e":"bookTicker","u":"x"}"""u8));
        Assert.Empty(log.Data);
    }

    [Fact]
    public void ConnectionEventsReachHandler()
    {
        StreamLog<BookTickerModel> log = new();
        BinanceStreamDispatcher<FuturesBookTickerParser, BookTickerModel, StreamLogHandler<BookTickerModel>> dispatcher = new(log.Handler, Capacity);
        WsStreamGap gap = new() { Reason = WsDisconnectReason.ExceptionSocket, StartUtcMillis = 1, EndUtcMillis = 2 };

        dispatcher.OnDisconnected(2, WsDisconnectReason.ExceptionSocket, isFatal: false);
        dispatcher.OnReconnected(2, in gap);
        dispatcher.OnUnsubscribed(2);

        Assert.Equal(
            [
                new HandlerEvent(HandlerEventKind.Disconnected, 2, WsDisconnectReason.ExceptionSocket, false, default),
                new HandlerEvent(HandlerEventKind.Reconnected, 2, WsDisconnectReason.None, false, gap),
                new HandlerEvent(HandlerEventKind.Unsubscribed, 2, WsDisconnectReason.None, false, default),
            ],
            log.Events);
    }

    [Fact]
    public void ClosingKlineIsDeliveredAndLateOpenCopyIsNot()
    {
        StreamLog<KlineUpdateModel> log = new();
        BinanceStreamDispatcher<FuturesKlineParser, KlineUpdateModel, StreamLogHandler<KlineUpdateModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        dispatcher.Dispatch(0, FuturesFrames.Kline);
        dispatcher.Dispatch(0, FuturesFrames.KlineClosed);

        // The other connection delivers the open push late: the final candle must not be overwritten.
        dispatcher.Dispatch(0, FuturesFrames.Kline);

        Assert.Equal([false, true], log.Data.Select(static record => record.Model.IsClosed).ToArray());
    }

    [Fact]
    public void DepthDropsOlderUpdatesAndGrowsLevelBuffers()
    {
        DepthLog log = new();
        BinanceDepthDispatcher<FuturesDepthUpdateParser, DepthLogHandler> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        // More levels than the starting buffer of 64.
        byte[] wide = Depth(first: 1, final: 2, previous: 0, bidCount: 70);

        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, wide));
        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, wide));
        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch(0, Depth(first: 3, final: 4, previous: 2, bidCount: 1)));

        Assert.Equal(2, log.Data.Count);
        Assert.Equal(70, log.Data[0].Bids.Length);
        Assert.Equal(931, log.Data[0].Bids[69].Price);
        Assert.Equal(4, log.Data[1].Header.FinalUpdateId);
    }

    [Fact]
    public void SbeTradesGoOneByOneAndOverlapIsCutById()
    {
        StreamLog<SbeTradeModel> log = new();
        BinanceSbeTradeDispatcher<StreamLogHandler<SbeTradeModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        dispatcher.Dispatch(0, SbeFrameBuilder.Trades("BTCUSDT", 1, 1, -2, -5, (1001, 6700001, 100000, true), (1002, 6700002, 100000, false)));

        // The other connection sends an event that overlaps the last one.
        dispatcher.Dispatch(0, SbeFrameBuilder.Trades("BTCUSDT", 2, 2, -2, -5, (1002, 6700002, 100000, false), (1003, 6700003, 100000, true)));

        Assert.Equal([1001L, 1002L, 1003L], log.Data.Select(static record => record.Model.TradeId).ToArray());
    }

    [Fact]
    public void BrokenSbeTradesAreUnreadable()
    {
        StreamLog<SbeTradeModel> log = new();
        BinanceSbeTradeDispatcher<StreamLogHandler<SbeTradeModel>> dispatcher = new(log.Handler, Capacity);
        dispatcher.OnSubscribing(0);

        byte[] frame = SbeFrameBuilder.Trades("BTCUSDT", 1, 1, -2, -5, (1001, 6700001, 100000, true));

        Assert.Equal(WsDispatchResult.Unreadable, dispatcher.Dispatch(0, frame.AsSpan(0, frame.Length - 1)));
        Assert.Empty(log.Data);
    }

    [Fact]
    public void WsApiServerShutdownAsksForRotation()
    {
        BinanceWsApiEventDispatcher dispatcher = default;

        Assert.Equal(WsDispatchResult.Handled, dispatcher.Dispatch("""{"event":{"e":"serverShutdown","E":1}}"""u8, 1, out WsInboundSignal signal));
        Assert.Equal(WsInboundSignal.ServerShutdown, signal);
    }

    [Theory]
    [InlineData("""{"subscriptionId":0,"event":{"e":"executionReport"}}""")]
    [InlineData("""{"event":"serverShutdown"}""")]
    [InlineData("not json")]
    public void WsApiOtherEventIsUnknown(string text)
    {
        BinanceWsApiEventDispatcher dispatcher = default;

        Assert.Equal(WsDispatchResult.Unreadable, dispatcher.Dispatch(Encoding.ASCII.GetBytes(text), 1, out WsInboundSignal signal));
        Assert.Equal(WsInboundSignal.None, signal);
    }

    private static byte[] Ticker(long updateId) => Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
        $$"""{"e":"bookTicker","u":{{updateId}},"s":"BNBUSDT","ps":"BNBUSDT","b":"25.3519","B":"31.21","a":"25.3652","A":"40.66","T":1,"E":1}"""));

    private static byte[] Depth(long first, long final, long previous, int bidCount)
    {
        StringBuilder body = new();
        body.Append(CultureInfo.InvariantCulture, $$"""{"e":"depthUpdate","E":1,"T":1,"s":"BTCUSDT","ps":"BTCUSDT","U":{{first}},"u":{{final}},"pu":{{previous}},"b":[""");

        for (int i = 0; i < bidCount; i++)
        {
            body.Append(i == 0 ? "" : ",");
            body.Append(CultureInfo.InvariantCulture, $$"""["{{1000 - i}}","1"]""");
        }

        body.Append("],\"a\":[]}");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    private static long[] UpdateIds(StreamLog<BookTickerModel> log) => log.Data.Select(static record => record.Model.UpdateId).ToArray();
}
