using System.Text;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Sbe;

/// <summary>Spot SBE streams read from mainnet with the Ed25519 key, and the raw frames they come in, written out for unit test fixtures.</summary>
public sealed class SbeLiveTests
{
    private const string BtcName = "BTCUSDT";
    private const int PartialLevels = 20;
    private const int TopLevels = 20;
    private const int TradeCount = 20;
    private const int SinkCapacity = 1_000;
    private const long MicrosPerMillisecond = 1_000;

    // Messages between two reads of a cache, so the second one is surely newer.
    private const int MessagesApart = 3;

    // About a second of updates at 20 ms: enough to catch a broken update id chain or a price read two ways.
    private const int BookUpdatesApart = 50;

    // Wide enough for the time between the event and the server time call; milliseconds read as microseconds miss it by far.
    private const long ClockToleranceMicros = 60_000_000;

    // Neighbor levels are at least a tick apart. Closer means the REST snapshot and the SBE updates read one price differently.
    private const double TwinRatio = 1e-9;

    private static readonly Symbol _btc = Symbol.Parse(BtcName);

    // Shaped like a real key, registered nowhere.
    private static readonly string _wrongKey = new('x', 64);

    private static readonly string[] _captureStreams = ["btcusdt@trade", "btcusdt@bestBidAsk", "btcusdt@depth", "btcusdt@depth20"];

    private static readonly int[] _templateIds =
    [
        BinanceSbeReader.TradesTemplateId,
        BinanceSbeReader.BestBidAskTemplateId,
        BinanceSbeReader.DepthDiffTemplateId,
        BinanceSbeReader.DepthSnapshotTemplateId,
    ];

    [Fact]
    public async Task BestBidAskIsUncrossedAndMovesOn()
    {
        LiveEnvironment.RequireEd25519Key();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        LatestCache<SbeBestBidAskModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.BestBidAsk.CreateSession(cache.Handler);

        int handle = await SubscribeAsync(session);
        (SbeBestBidAskModel first, SbeBestBidAskModel latest) = await ReadTwiceAsync(cache, handle);
        long serverMicros = await ServerMicrosAsync(client);

        Assert.Equal(_btc, latest.Symbol);
        Assert.True(first.UpdateId < latest.UpdateId, "The update id did not grow.");
        AssertNearServerTime(serverMicros, latest.EventTimeMicros);
        AssertUncrossed(latest.BidPrice, latest.BidQuantity, latest.AskPrice, latest.AskQuantity);
        log.AssertContractKept();
    }

    [Fact]
    public async Task TradesComeOneByOneWithNoneLost()
    {
        LiveEnvironment.RequireEd25519Key();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        ChannelSink<SbeTradeModel> sink = new(SinkCapacity, client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.Trade.CreateSession(sink.Handler);

        await SubscribeAsync(session);
        SbeTradeModel[] trades = await ReadTradesAsync(sink);
        long serverMicros = await ServerMicrosAsync(client);

        AssertNearServerTime(serverMicros, trades[0].EventTimeMicros);

        for (int i = 0; i < trades.Length; i++)
        {
            SbeTradeModel trade = trades[i];

            Assert.Equal(_btc, trade.Symbol);
            Assert.True(0 < trade.Price && 0 < trade.Quantity, $"Trade {trade.TradeId} is not positive.");
            Assert.True(trade.TransactionTimeMicros <= trade.EventTimeMicros, $"Trade {trade.TradeId} happened after its event.");

            if (0 < i)
            {
                SbeTradeModel previous = trades[i - 1];

                // Trade ids of a symbol run without gaps: a trade dropped from an event of several breaks this.
                Assert.Equal(previous.TradeId + 1, trade.TradeId);
                Assert.True(previous.TransactionTimeMicros <= trade.TransactionTimeMicros, $"Trade {trade.TradeId} is older than {previous.TradeId}.");
            }
        }

        log.AssertContractKept();
    }

    [Fact]
    public async Task PartialDepthIsFullSortedAndUncrossed()
    {
        LiveEnvironment.RequireEd25519Key();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        LatestCache<SbePartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.PartialDepth.CreateSession(cache.Handler);

        int handle = await SubscribeAsync(session);
        (SbePartialDepthModel first, SbePartialDepthModel latest) = await ReadTwiceAsync(cache, handle);
        long serverMicros = await ServerMicrosAsync(client);

        Assert.Equal(_btc, latest.Symbol);
        Assert.True(first.LastUpdateId < latest.LastUpdateId, "The update id did not grow.");
        AssertNearServerTime(serverMicros, latest.EventTimeMicros);
        Assert.Equal(PartialLevels, latest.BidCount);
        Assert.Equal(PartialLevels, latest.AskCount);
        AssertSortedAndUncrossed(latest.Bids[..latest.BidCount], latest.Asks[..latest.AskCount]);
        log.AssertContractKept();
    }

    [Fact]
    public async Task OrderBookSyncsFromRestSnapshotAndStaysInSync()
    {
        LiveEnvironment.RequireEd25519Key();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot);
        OrderBookSet<LiveTelemetry> books = new(client, BinanceMarkets.Spot);
        BinanceStreamSession session = client.Spot.SbeStreams.Depth.CreateSession(books.Handler);
        DepthLevel[] bids = new DepthLevel[TopLevels];
        DepthLevel[] asks = new DepthLevel[TopLevels];

        int handle = await SubscribeAsync(session);
        await LiveEnvironment.UntilAsync(() => books.IsSynced(handle));

        long version = books.GetVersion(handle);
        await LiveEnvironment.UntilAsync(() => version + BookUpdatesApart <= books.GetVersion(handle));

        Assert.True(books.TryCopyTop(handle, bids, asks, out int bidCount, out int askCount, out long updateId), "The book lost its sync.");
        Assert.Equal(TopLevels, bidCount);
        Assert.Equal(TopLevels, askCount);
        Assert.True(0 < updateId);

        // The snapshot comes from REST as JSON, the updates as SBE mantissas: a price read differently leaves a stale twin level.
        AssertSortedAndUncrossed(bids, asks);

        // A gap in the update ids of a healthy connection means the chain is read wrong.
        Assert.Equal(0, books.ResyncCount);
        Assert.False(books.Completion.IsFaulted);
        Assert.False(session.Completion.IsFaulted);
        log.AssertContractKept();
    }

    [Fact]
    public async Task WrongKeyStopsSessionAtHandshake()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, streamMarkets: BinanceMarkets.Spot, ed25519ApiKey: _wrongKey);
        LatestCache<SbeBestBidAskModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.BestBidAsk.CreateSession(cache.Handler);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Refused for good on the first handshake: no reconnect loop eating the connection attempts of the IP.
        WsStreamFatalException fatal = await Assert.ThrowsAsync<WsStreamFatalException>(() => session.SubscribeAsync(_btc, cancellationToken).AsTask());

        Assert.Equal(WsDisconnectReason.HandshakeRejected, fatal.Disconnect.Reason);
        await Assert.ThrowsAsync<WsStreamFatalException>(() => session.Completion.WaitAsync(LiveEnvironment.WaitLimit, cancellationToken));
    }

    [Fact]
    public async Task RawFramesOfEveryTemplateRouteAndParse()
    {
        LiveEnvironment.RequireEd25519Key();
        LiveTelemetryLog log = new();

        Dictionary<int, byte[]> frames = await LiveCapture.FirstFramesAsync(LiveEnvironment.Ed25519ApiKey, _captureStreams, _templateIds);

        // Written out before any assert, so a failed run still gives the fixtures.
        foreach (int templateId in _templateIds)
        {
            LiveCapture.Report(log, templateId, frames[templateId]);
        }

        foreach (int templateId in _templateIds)
        {
            byte[] frame = frames[templateId];

            Assert.Equal(WsInboundKind.Push, BinanceDialectSbeStream.ReadInbound(frame, isBinary: true, out WsStreamInbound inbound));
            Assert.Equal(templateId, inbound.Channel);
            Assert.Equal(BtcName, Encoding.ASCII.GetString(frame, inbound.KeyStart, inbound.KeyLength));
            Assert.True(Parses(templateId, frame), $"Template {templateId} did not parse.");
        }
    }

    private static async Task<int> SubscribeAsync(BinanceStreamSession session)
    {
        WsStreamSubscribeResult result = await session.SubscribeAsync(_btc, TestContext.Current.CancellationToken);

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, result.Status);
        return result.Handle;
    }

    /// <summary>Server time in microseconds, the unit of every SBE time.</summary>
    private static async Task<long> ServerMicrosAsync(BinanceClient<LiveTelemetry> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Spot.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        return result.Value.ServerTime * MicrosPerMillisecond;
    }

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
    private static async Task<SbeTradeModel[]> ReadTradesAsync(ChannelSink<SbeTradeModel> sink)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SbeTradeModel[] trades = new SbeTradeModel[TradeCount];
        int count = 0;

        while (count < trades.Length)
        {
            bool open = await sink.WaitToReadAsync(cancellationToken).AsTask().WaitAsync(LiveEnvironment.WaitLimit, cancellationToken);
            Assert.True(open, "The sink ended.");

            while (count < trades.Length && sink.TryRead(out StreamItem<SbeTradeModel> item))
            {
                Assert.Equal(StreamItemKind.Data, item.Kind);
                trades[count++] = item.Model;
            }
        }

        return trades;
    }

    /// <summary>Whether the parser of the template reads the frame.</summary>
    private static bool Parses(int templateId, byte[] frame)
    {
        DepthLevel[] bids = new DepthLevel[PartialLevels];
        DepthLevel[] asks = new DepthLevel[PartialLevels];

        return templateId switch
        {
            BinanceSbeReader.TradesTemplateId => SbeTradeParser.TryOpen(frame, out _, out _, out _, out _, out _, out _),
            BinanceSbeReader.BestBidAskTemplateId => SbeBestBidAskParser.TryParse(frame, out _, out _),
            BinanceSbeReader.DepthSnapshotTemplateId => SbePartialDepthParser.TryParse(frame, out _, out _),
            BinanceSbeReader.DepthDiffTemplateId => SbeDepthUpdateParser.TryParse(frame, out _, ref bids, out _, ref asks, out _),
            _ => false,
        };
    }

    private static void AssertNearServerTime(long serverMicros, long micros) =>
        Assert.InRange(micros, serverMicros - ClockToleranceMicros, serverMicros + ClockToleranceMicros);

    /// <summary>Both sides are positive and the best bid is below the best ask.</summary>
    private static void AssertUncrossed(double bidPrice, double bidQuantity, double askPrice, double askQuantity)
    {
        Assert.True(0 < bidPrice && 0 < bidQuantity && 0 < askPrice && 0 < askQuantity, "A side is not positive.");
        Assert.True(bidPrice < askPrice, "The book is crossed.");
    }

    /// <summary>Bids go down, asks go up, neighbors are at least a tick apart, every level is positive and the best bid is below the best ask.</summary>
    private static void AssertSortedAndUncrossed(ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        for (int i = 0; i < bids.Length; i++)
        {
            Assert.True(0 < bids[i].Price && 0 < bids[i].Quantity, $"Bid {i} is not positive.");
            Assert.True(i == 0 || bids[i].Price < bids[i - 1].Price * (1 - TwinRatio), $"Bid {i} is out of order or a twin of the one above.");
        }

        for (int i = 0; i < asks.Length; i++)
        {
            Assert.True(0 < asks[i].Price && 0 < asks[i].Quantity, $"Ask {i} is not positive.");
            Assert.True(i == 0 || asks[i - 1].Price * (1 + TwinRatio) < asks[i].Price, $"Ask {i} is out of order or a twin of the one below.");
        }

        Assert.True(bids[0].Price < asks[0].Price, "The book is crossed.");
    }
}
