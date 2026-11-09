using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>Spot SBE streams against a local exchange: the key in the handshake, routing by template, the order book over SBE, rotation and the key guard.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class SbeStreamTests
{
    private const string SbePath = FakeExchangeServer.SpotSbePath;
    private const string ApiKeyHeader = "X-MBX-APIKEY";
    private const string Ed25519Key = "ed25519-test-key";
    private const string DepthPath = "/api/v3/depth";
    private const long EventTimeMicros = 1700000000123456;

    // Spot sends no event or transaction time in the snapshot.
    private const string SpotSnapshot = """{"lastUpdateId":100,"bids":[["100","1"],["99","2"]],"asks":[["101","1"]]}""";

    // Spot announces a restart ten minutes before it drops the connection; on SBE it comes as JSON in a text frame.
    private const string ServerShutdown = """{"stream":"!serverShutdown","data":{"e":"serverShutdown","E":1764079200000}}""";

    private static readonly Symbol _btc = Symbol.Parse("BTCUSDT");
    private static readonly string _serverTimeBody = Encoding.UTF8.GetString(RestBodies.ServerTime);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachSessionGetsOnlyItsTemplate()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<SbeBestBidAskModel> quotes = new();
        StreamLog<SbeTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeStreamRoute route = server.Stream(SbePath);

        BinanceStreamSession quoteSession = client.Spot.SbeStreams.BestBidAsk.CreateSession(quotes.Handler);
        BinanceStreamSession tradeSession = client.Spot.SbeStreams.Trade.CreateSession(trades.Handler);

        WsStreamSubscribeResult quote = await quoteSession.SubscribeAsync(_btc, Token);
        WsStreamSubscribeResult trade = await tradeSession.SubscribeAsync(_btc, Token);

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, quote.Status);
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, trade.Status);
        Assert.True(route.IsSubscribed("btcusdt@bestBidAsk"));
        Assert.True(route.IsSubscribed("btcusdt@trade"));

        // Each session has its own connection, and each handshake carries the Ed25519 key, not the HMAC one.
        IReadOnlyList<FakeConnection> connections = route.Connections;
        Assert.Equal(2, connections.Count);
        Assert.All(connections, connection => Assert.Equal(Ed25519Key, connection.Headers[ApiKeyHeader]));

        // Both frames go to both connections: binary data has no stream name, so the template id decides.
        byte[] quoteFrame = SbeFrameBuilder.BestBidAsk("BTCUSDT", EventTimeMicros, 9001, 0, 0, 67000, 3, 67001, 2);
        byte[] tradeFrame = SbeFrameBuilder.Trades("BTCUSDT", EventTimeMicros, EventTimeMicros - 5, 0, 0, (1001, 67000, 2, true), (1002, 67001, 1, false));

        foreach (FakeConnection connection in connections)
        {
            await connection.SendBinaryAsync(quoteFrame);
            await connection.SendBinaryAsync(tradeFrame);
        }

        await Wait.UntilAsync(() => quotes.Data.Count == 1 && trades.Data.Count == 2 && log.Count(TelemetryEventKind.WsStreamMessageUnknown) == 2);

        StreamRecord<SbeBestBidAskModel> best = Assert.Single(quotes.Data);
        Assert.Equal(quote.Handle, best.Subscription);
        Assert.Equal(_btc, best.Model.Symbol);
        Assert.Equal(9001, best.Model.UpdateId);
        Assert.Equal(EventTimeMicros, best.Model.EventTimeMicros);
        Assert.Equal(67000, best.Model.BidPrice);
        Assert.Equal(67001, best.Model.AskPrice);

        // The trades of one event arrive one by one, in order.
        IReadOnlyList<StreamRecord<SbeTradeModel>> fills = trades.Data;
        Assert.All(fills, fill => Assert.Equal(trade.Handle, fill.Subscription));
        Assert.Equal(1001, fills[0].Model.TradeId);
        Assert.Equal(67000, fills[0].Model.Price);
        Assert.Equal(1002, fills[1].Model.TradeId);
        Assert.Equal(67001, fills[1].Model.Price);
    }

    [Fact]
    public async Task DepthOverSbeSyncsOrderBookFromRestSnapshot()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        FakeHttpHandler http = Http();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, http);
        FakeStreamRoute route = server.Stream(SbePath);

        OrderBookSet<TelemetryLogHandler> set = new(client, BinanceMarkets.Spot, new OrderBookSetOptions() { SnapshotLimit = 5 });
        BinanceStreamSession session = client.Spot.SbeStreams.Depth.CreateSession(set.Handler);
        WsStreamSubscribeResult depth = await session.SubscribeAsync(_btc, Token);

        // Spot chains updates by U = previous u + 1: the first one after snapshot 100 starts at 101.
        FakeConnection connection = Assert.Single(route.Connections);
        await connection.SendBinaryAsync(SbeFrameBuilder.DepthDiff("BTCUSDT", EventTimeMicros, 101, 101, 0, 0, [(100, 5)], []));

        await Wait.UntilAsync(() => set.IsSynced(depth.Handle));

        Assert.True(set.TryGetBest(depth.Handle, out DepthLevel bid, out DepthLevel ask, out long updateId));
        Assert.Equal(100, bid.Price);
        Assert.Equal(5, bid.Quantity);
        Assert.Equal(101, ask.Price);
        Assert.Equal(101, updateId);
        Assert.Equal(1, http.Count(DepthPath));

        // The next update removes the best bid: the level below takes its place.
        await connection.SendBinaryAsync(SbeFrameBuilder.DepthDiff("BTCUSDT", EventTimeMicros + 100, 102, 102, 0, 0, [(100, 0)], []));
        await Wait.UntilAsync(() => set.TryGetBest(depth.Handle, out _, out _, out long id) && id == 102);

        Assert.True(set.TryGetBest(depth.Handle, out bid, out _, out _));
        Assert.Equal(99, bid.Price);
        Assert.Equal(2, bid.Quantity);
    }

    [Fact]
    public async Task ServerShutdownRotatesSbeConnection()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<SbeBestBidAskModel> quotes = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeStreamRoute route = server.Stream(SbePath);
        BinanceStreamSession session = client.Spot.SbeStreams.BestBidAsk.CreateSession(quotes.Handler);

        WsStreamSubscribeResult quote = await session.SubscribeAsync(_btc, Token);
        await route.BroadcastAsync(ServerShutdown);

        // Replaced make-before-break: the new connection carries the key and is subscribed before the old one closes.
        await Wait.UntilAsync(() => route.Connections.Count == 2 && !route.Connections[0].IsOpen && route.IsSubscribed("btcusdt@bestBidAsk"));

        FakeConnection fresh = route.Connections[1];
        Assert.Equal(Ed25519Key, fresh.Headers[ApiKeyHeader]);

        await fresh.SendBinaryAsync(SbeFrameBuilder.BestBidAsk("BTCUSDT", EventTimeMicros, 9002, 0, 0, 67000, 3, 67001, 2));
        await Wait.UntilAsync(() => quotes.Data.Count == 1);

        Assert.Equal(quote.Handle, quotes.Data[0].Subscription);
        Assert.Empty(quotes.Events);
        Assert.Contains(log.Of(TelemetryEventKind.WsStreamDisconnected), item => item.Disconnect.Reason == WsDisconnectReason.ServerShutdown);
    }

    [Fact]
    public async Task MissingEd25519KeyRefusesEverySbeSession()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, withSbeKey: false);
        OrderBookSet<TelemetryLogHandler> set = new(client, BinanceMarkets.Spot);

        // Spot streams are on, the key is not: refused at once rather than handshakes the exchange will reject.
        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.BestBidAsk.CreateSession(new StreamLog<SbeBestBidAskModel>().Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.Trade.CreateSession(new StreamLog<SbeTradeModel>().Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.PartialDepth.CreateSession(new StreamLog<SbePartialDepthModel>().Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.Depth.CreateSession(set.Handler));

        Assert.Empty(server.Stream(SbePath).Connections);

        // Refused before binding: the set can still serve a spot session.
        set.Handler.Bind(BinanceMarkets.Spot);
    }

    /// <summary>Client with spot streams on the fake exchange and, unless told otherwise, the Ed25519 key for SBE.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(FakeExchangeServer server, TelemetryLog log, FakeHttpHandler? http = null, bool withSbeKey = true)
    {
        BinanceOptions options = TestClient.Options(server, BinanceMarkets.None, BinanceMarkets.Spot);

        if (withSbeKey)
        {
            options = options with { Secret = new BinanceSecret(TestClient.ApiKey, TestClient.ApiSecret) { Ed25519ApiKey = Ed25519Key } };
        }

        return TestClient.Create(http ?? Http(), log.Handler, Token, options, TestClient.WsEngine());
    }

    /// <summary>Answers spot depth snapshots, and server time for anything else.</summary>
    private static FakeHttpHandler Http() =>
        new((request, _) => Task.FromResult(FakeHttpHandler.Json(
            HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath == DepthPath ? SpotSnapshot : _serverTimeBody)));
}
