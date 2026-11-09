using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Public;

/// <summary>Public market streams of every market, read from mainnet without keys.</summary>
public sealed class StreamLiveTests
{
    private const int PartialLevels = 20;
    private const int TopLevels = 20;
    private const int TradeCount = 20;
    private const int SinkCapacity = 1_000;
    private const long OneMinuteMillis = 60_000;

    // Messages between two reads of a cache, so the second one is surely newer.
    private const int MessagesApart = 3;

    // Updates applied on top of the snapshot, enough to catch a broken update id chain.
    private const int BookUpdatesApart = 10;

    private static readonly Symbol _btc = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _eth = Symbol.Parse("ETHUSDT");

    private static readonly Symbol[] _symbols =
    [
        Symbol.Parse("BTCUSDT"),
        Symbol.Parse("ETHUSDT"),
        Symbol.Parse("BNBUSDT"),
        Symbol.Parse("SOLUSDT"),
        Symbol.Parse("XRPUSDT"),
    ];

    [Fact]
    public async Task UsdMBookTickerIsUncrossedAndMovesOn()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.FuturesUsdM);
        LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);

        int handle = await SubscribeAsync(session, _btc);
        (BookTickerModel first, BookTickerModel latest) = await ReadTwiceAsync(cache, handle);

        Assert.Equal(_btc, latest.Symbol);
        Assert.True(first.UpdateId < latest.UpdateId, "The update id did not grow.");
        Assert.True(0 < latest.EventTime && 0 < latest.TransactionTime);
        AssertUncrossed(latest.BidPrice, latest.BidQuantity, latest.AskPrice, latest.AskQuantity);
        log.AssertContractKept();
    }

    [Fact]
    public async Task SpotBookTickerIsUncrossedAndMovesOn()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        LatestCache<SpotBookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.WsStreams.BookTicker.CreateSession(cache.Handler);

        int handle = await SubscribeAsync(session, _btc);
        (SpotBookTickerModel first, SpotBookTickerModel latest) = await ReadTwiceAsync(cache, handle);

        Assert.Equal(_btc, latest.Symbol);
        Assert.True(first.UpdateId < latest.UpdateId, "The update id did not grow.");
        AssertUncrossed(latest.BidPrice, latest.BidQuantity, latest.AskPrice, latest.AskQuantity);
        log.AssertContractKept();
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.Spot)]
    public async Task AggTradesComeInOrder(BinanceMarkets market)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: market);
        ChannelSink<AggTradeModel> sink = new(SinkCapacity, client.StreamSessionCapacity);

        BinanceStreamSession session = market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.WsStreams.AggTrade.CreateSession(sink.Handler),
            BinanceMarkets.Spot => client.Spot.WsStreams.AggTrade.CreateSession(sink.Handler),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };

        await SubscribeAsync(session, _btc);
        AggTradeModel[] trades = await ReadTradesAsync(sink);

        for (int i = 0; i < trades.Length; i++)
        {
            AggTradeModel trade = trades[i];

            Assert.Equal(_btc, trade.Symbol);
            Assert.True(0 < trade.Price && 0 < trade.Quantity, $"Trade {trade.AggregateTradeId} is not positive.");
            Assert.True(trade.FirstTradeId <= trade.LastTradeId, $"Trade {trade.AggregateTradeId} has its trade ids reversed.");

            if (0 < i)
            {
                AggTradeModel previous = trades[i - 1];

                // A duplicate or a reordered trade breaks these.
                Assert.True(previous.AggregateTradeId < trade.AggregateTradeId, $"Trade {trade.AggregateTradeId} came after {previous.AggregateTradeId}.");
                Assert.True(previous.LastTradeId < trade.FirstTradeId, $"Trade {trade.AggregateTradeId} overlaps {previous.AggregateTradeId}.");
                Assert.True(previous.TradeTime <= trade.TradeTime, $"Trade {trade.AggregateTradeId} is older than {previous.AggregateTradeId}.");
            }
        }

        log.AssertContractKept();
    }

    [Fact]
    public async Task UsdMKlineIsCurrentMinuteCandle()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.FuturesUsdM);
        LatestCache<KlineUpdateModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.Kline.CreateSession(cache.Handler, BinanceInterval.OneMinute);

        int handle = await SubscribeAsync(session, _btc);
        KlineUpdateModel update = await ReadLiveAsync(cache, handle);
        Kline candle = update.Kline;

        Assert.Equal(_btc, update.Symbol);
        Assert.Equal(0, candle.OpenTime % OneMinuteMillis);
        Assert.Equal(candle.OpenTime + OneMinuteMillis - 1, candle.CloseTime);

        // The closing push comes a little after the close time.
        Assert.InRange(update.EventTime, candle.OpenTime, candle.CloseTime + OneMinuteMillis);
        Assert.True(candle.Low <= Math.Min(candle.Open, candle.Close) && Math.Max(candle.Open, candle.Close) <= candle.High, "The candle has prices outside its range.");
        Assert.True(0 <= candle.TakerBuyVolume && candle.TakerBuyVolume <= candle.Volume, "The candle has taker volume above its volume.");
        log.AssertContractKept();
    }

    [Fact]
    public async Task UsdMPartialDepthIsFullSortedAndUncrossed()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.FuturesUsdM);
        LatestCache<PartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.PartialDepth.CreateSession(cache.Handler, PartialDepthLevels.Twenty, DepthUpdateSpeed.Ms100);

        int handle = await SubscribeAsync(session, _btc);
        (PartialDepthModel first, PartialDepthModel latest) = await ReadTwiceAsync(cache, handle);

        Assert.Equal(_btc, latest.Symbol);
        Assert.True(first.FinalUpdateId < latest.FinalUpdateId, "The update id did not grow.");
        Assert.True(0 < latest.EventTime && 0 < latest.TransactionTime);
        Assert.Equal(PartialLevels, latest.BidCount);
        Assert.Equal(PartialLevels, latest.AskCount);
        AssertSortedAndUncrossed(latest.Bids[..latest.BidCount], latest.Asks[..latest.AskCount]);
        log.AssertContractKept();
    }

    [Fact]
    public async Task SpotPartialDepthIsFullSortedAndUncrossed()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        LatestCache<SpotPartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.WsStreams.PartialDepth.CreateSession(cache.Handler, PartialDepthLevels.Twenty, SpotDepthUpdateSpeed.Ms100);

        int handle = await SubscribeAsync(session, _btc);
        (SpotPartialDepthModel first, SpotPartialDepthModel latest) = await ReadTwiceAsync(cache, handle);

        Assert.True(first.LastUpdateId < latest.LastUpdateId, "The update id did not grow.");
        Assert.Equal(PartialLevels, latest.BidCount);
        Assert.Equal(PartialLevels, latest.AskCount);
        AssertSortedAndUncrossed(latest.Bids[..latest.BidCount], latest.Asks[..latest.AskCount]);
        log.AssertContractKept();
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM, "BTCUSDT")]
    [InlineData(BinanceMarkets.FuturesCoinM, "BTCUSD_PERP")]
    [InlineData(BinanceMarkets.Spot, "BTCUSDT")]
    public async Task OrderBookSyncsAndStaysInSync(BinanceMarkets market, string symbol)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: market);
        OrderBookSet<LiveTelemetry> books = new(client, market);
        BinanceStreamSession session = DepthSession(client, market, books);
        DepthLevel[] bids = new DepthLevel[TopLevels];
        DepthLevel[] asks = new DepthLevel[TopLevels];

        int handle = await SubscribeAsync(session, Symbol.Parse(symbol));
        await LiveEnvironment.UntilAsync(() => books.IsSynced(handle));

        long version = books.GetVersion(handle);
        await LiveEnvironment.UntilAsync(() => version + BookUpdatesApart <= books.GetVersion(handle));

        Assert.True(books.TryCopyTop(handle, bids, asks, out int bidCount, out int askCount, out long updateId), "The book lost its sync.");
        Assert.Equal(TopLevels, bidCount);
        Assert.Equal(TopLevels, askCount);
        Assert.True(0 < updateId);
        AssertSortedAndUncrossed(bids, asks);

        // A gap in the update ids of a healthy connection means the chain is read wrong.
        Assert.Equal(0, books.ResyncCount);
        Assert.False(books.Completion.IsFaulted);
        Assert.False(session.Completion.IsFaulted);
        log.AssertContractKept();
    }

    [Fact]
    public async Task EverySubscriptionGetsItsOwnSymbol()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.FuturesUsdM);
        LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);
        int[] handles = new int[_symbols.Length];

        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = await SubscribeAsync(session, _symbols[i]);
        }

        await LiveEnvironment.UntilAsync(() => AllLive(cache, handles));

        for (int i = 0; i < handles.Length; i++)
        {
            Assert.True(cache.TryRead(handles[i], out BookTickerModel ticker, out _));
            Assert.Equal(_symbols[i], ticker.Symbol);
        }

        Assert.Distinct(handles);
        log.AssertContractKept();
    }

    [Fact]
    public async Task UnsubscribedStreamLeavesNothingBehind()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.FuturesUsdM);
        LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);

        int btc = await SubscribeAsync(session, _btc);
        await ReadLiveAsync(cache, btc);

        Assert.True(await session.UnsubscribeAsync(btc, TestContext.Current.CancellationToken));
        Assert.False(cache.TryReadLast(btc, out _, out _));

        // The handle may go to the new symbol: messages of the old stream still in flight must not land on it.
        int eth = await SubscribeAsync(session, _eth);
        (BookTickerModel first, BookTickerModel latest) = await ReadTwiceAsync(cache, eth);

        Assert.Equal(_eth, first.Symbol);
        Assert.Equal(_eth, latest.Symbol);

        // The tail of the old stream comes as unknown messages after unsubscribe, so the contract is not asserted here.
    }

    private static async Task<int> SubscribeAsync(BinanceStreamSession session, Symbol symbol)
    {
        WsStreamSubscribeResult result = await session.SubscribeAsync(symbol, TestContext.Current.CancellationToken);

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, result.Status);
        return result.Handle;
    }

    private static BinanceStreamSession DepthSession(BinanceClient<LiveTelemetry> client, BinanceMarkets market, OrderBookSet<LiveTelemetry> books) =>
        market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.WsStreams.Depth.CreateSession(books.Handler, DepthUpdateSpeed.Ms100),
            BinanceMarkets.FuturesCoinM => client.Futures.CoinM.WsStreams.Depth.CreateSession(books.Handler, DepthUpdateSpeed.Ms100),
            BinanceMarkets.Spot => client.Spot.WsStreams.Depth.CreateSession(books.Handler, SpotDepthUpdateSpeed.Ms100),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };

    /// <summary>Waits for the subscription to go live and returns its latest message.</summary>
    private static async Task<TModel> ReadLiveAsync<TModel>(LatestCache<TModel> cache, int handle) where TModel : struct
    {
        await LiveEnvironment.UntilAsync(() => cache.TryRead(handle, out _, out _));

        Assert.True(cache.TryRead(handle, out TModel model, out _));
        return model;
    }

    /// <summary>The first live message and one a few messages later.</summary>
    private static async Task<(TModel First, TModel Latest)> ReadTwiceAsync<TModel>(LatestCache<TModel> cache, int handle) where TModel : struct
    {
        TModel first = await ReadLiveAsync(cache, handle);
        long version = cache.GetVersion(handle);

        await LiveEnvironment.UntilAsync(() => version + MessagesApart <= cache.GetVersion(handle));
        return (first, await ReadLiveAsync(cache, handle));
    }

    /// <summary>Reads the first trades of the sink. A connection event fails the test: trades inside a gap are lost.</summary>
    private static async Task<AggTradeModel[]> ReadTradesAsync(ChannelSink<AggTradeModel> sink)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AggTradeModel[] trades = new AggTradeModel[TradeCount];
        int count = 0;

        while (count < trades.Length)
        {
            bool open = await sink.WaitToReadAsync(cancellationToken).AsTask().WaitAsync(LiveEnvironment.WaitLimit, cancellationToken);
            Assert.True(open, "The sink ended.");

            while (count < trades.Length && sink.TryRead(out StreamItem<AggTradeModel> item))
            {
                Assert.Equal(StreamItemKind.Data, item.Kind);
                trades[count++] = item.Model;
            }
        }

        return trades;
    }

    private static bool AllLive<TModel>(LatestCache<TModel> cache, int[] handles) where TModel : struct
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (!cache.TryRead(handles[i], out _, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Both sides are positive and the best bid is below the best ask.</summary>
    private static void AssertUncrossed(double bidPrice, double bidQuantity, double askPrice, double askQuantity)
    {
        Assert.True(0 < bidPrice && 0 < bidQuantity && 0 < askPrice && 0 < askQuantity, "A side is not positive.");
        Assert.True(bidPrice < askPrice, "The book is crossed.");
    }

    /// <summary>Bids go down, asks go up, every level is positive and the best bid is below the best ask.</summary>
    private static void AssertSortedAndUncrossed(ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        for (int i = 0; i < bids.Length; i++)
        {
            Assert.True(0 < bids[i].Price && 0 < bids[i].Quantity, $"Bid {i} is not positive.");
            Assert.True(i == 0 || bids[i].Price < bids[i - 1].Price, $"Bid {i} is out of order.");
        }

        for (int i = 0; i < asks.Length; i++)
        {
            Assert.True(0 < asks[i].Price && 0 < asks[i].Quantity, $"Ask {i} is not positive.");
            Assert.True(i == 0 || asks[i - 1].Price < asks[i].Price, $"Ask {i} is out of order.");
        }

        Assert.True(bids[0].Price < asks[0].Price, "The book is crossed.");
    }
}
