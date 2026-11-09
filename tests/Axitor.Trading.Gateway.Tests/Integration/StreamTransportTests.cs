using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>Stream connections against a local exchange: delivery by handle, reconnects with gaps, rotations without them, refusals and fatals.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class StreamTransportTests
{
    private const string MarketPath = FakeExchangeServer.FuturesMarketPath;
    private const string BtcTrades = "btcusdt@aggTrade";
    private const string EthTrades = "ethusdt@aggTrade";

    // Spot announces a restart ten minutes before it drops the connection.
    private const string ServerShutdown = """{"stream":"!serverShutdown","data":{"e":"serverShutdown","E":1764079200000}}""";

    private static readonly Symbol _btc = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _eth = Symbol.Parse("ETHUSDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubscribedStreamsDeliverByHandle()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);
        WsStreamSubscribeResult eth = await session.SubscribeAsync(_eth, Token);

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, btc.Status);
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, eth.Status);
        Assert.NotEqual(btc.Handle, eth.Handle);

        await route.PushAsync(EthTrades, FuturesTrade("ETHUSDT", 7));
        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", 9));
        await Wait.UntilAsync(() => trades.Data.Count == 2);

        IReadOnlyList<StreamRecord<AggTradeModel>> data = trades.Data;
        Assert.Equal(eth.Handle, data[0].Subscription);
        Assert.Equal(_eth, data[0].Model.Symbol);
        Assert.Equal(7, data[0].Model.AggregateTradeId);
        Assert.Equal(btc.Handle, data[1].Subscription);
        Assert.Equal(_btc, data[1].Model.Symbol);
        Assert.Equal(9, data[1].Model.AggregateTradeId);

        // Both streams share one connection: the next one opens only when the first is full.
        Assert.Single(route.Connections);
        Assert.Equal(1, log.Count(TelemetryEventKind.WsStreamConnected));
    }

    [Fact]
    public async Task UnsubscribedStreamGetsNothingMore()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);

        Assert.True(await session.UnsubscribeAsync(btc.Handle, Token));
        Assert.False(route.IsSubscribed(BtcTrades));

        HandlerEvent unsubscribed = Assert.Single(trades.Events);
        Assert.Equal(HandlerEventKind.Unsubscribed, unsubscribed.Kind);
        Assert.Equal(btc.Handle, unsubscribed.Subscription);

        // A late message from the exchange is dropped as unknown and never reaches a handle that may be reused.
        await route.BroadcastAsync(FakeStreamRoute.Envelope(BtcTrades, FuturesTrade("BTCUSDT", 1)));
        await log.WaitForAsync(TelemetryEventKind.WsStreamMessageUnknown);

        Assert.Empty(trades.Data);
    }

    [Fact]
    public async Task ServerCloseReconnectsAndResubscribes()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);
        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", 1));
        await Wait.UntilAsync(() => trades.Data.Count == 1);

        await route.Connections[0].CloseAsync();

        // The stream is subscribed again on a new connection with no call from the user.
        await Wait.UntilAsync(() => route.Connections.Count == 2 && route.IsSubscribed(BtcTrades));
        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", 2));
        await Wait.UntilAsync(() => trades.Data.Count == 2 && trades.Events.Count == 2);

        IReadOnlyList<HandlerEvent> events = trades.Events;
        Assert.Equal(HandlerEventKind.Disconnected, events[0].Kind);
        Assert.Equal(btc.Handle, events[0].Subscription);
        Assert.Equal(WsDisconnectReason.ClosedByServer, events[0].Reason);
        Assert.False(events[0].IsFatal);
        Assert.Equal(HandlerEventKind.Reconnected, events[1].Kind);
        Assert.Equal(WsDisconnectReason.ClosedByServer, events[1].Gap.Reason);
        Assert.True(events[1].Gap.StartUtcMillis <= events[1].Gap.EndUtcMillis);
        Assert.Equal(2, trades.Data[1].Model.AggregateTradeId);
    }

    [Fact]
    public async Task OversizedMessageReconnects()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, engine: Engine(maxMessageBytes: 128 * 1024));
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        await session.SubscribeAsync(_btc, Token);

        // Twice the ceiling: the connection closes rather than drop the message silently.
        await route.PushAsync(BtcTrades, "\"" + new string('x', 256 * 1024) + "\"");
        await Wait.UntilAsync(() => route.Connections.Count == 2 && route.IsSubscribed(BtcTrades) && trades.Events.Count == 2);

        IReadOnlyList<HandlerEvent> events = trades.Events;
        Assert.Equal(HandlerEventKind.Disconnected, events[0].Kind);
        Assert.Equal(WsDisconnectReason.MessageTooBig, events[0].Reason);
        Assert.Equal(HandlerEventKind.Reconnected, events[1].Kind);
        Assert.Empty(trades.Data);
        Assert.Contains(log.Of(TelemetryEventKind.WsStreamDisconnected), item => item.Disconnect.Reason == WsDisconnectReason.MessageTooBig);
    }

    [Fact]
    public async Task UnansweredSubscribeStaysRegistered()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        route.IgnoreNext();
        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);

        Assert.Equal(WsStreamSubscribeStatus.Unconfirmed, btc.Status);
        Assert.True(0 <= btc.Handle);

        // The exchange applied it without a reply: its data still finds the handle.
        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", 1));
        await Wait.UntilAsync(() => trades.Data.Count == 1);

        Assert.Equal(btc.Handle, trades.Data[0].Subscription);
    }

    [Fact]
    public async Task InvalidStreamReplyIsFatal()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);

        // 2: Invalid request: invalid stream. A name the exchange does not know is a bug that retrying cannot fix.
        server.Stream(MarketPath).FailNext(2, "Invalid request: invalid stream");

        WsStreamFatalException fatal = await Assert.ThrowsAsync<WsStreamFatalException>(async () => await session.SubscribeAsync(_btc, Token));

        Assert.Equal(2, fatal.ExchangeCode);
        Assert.Equal(WsDisconnectReason.ExchangeFatal, fatal.Disconnect.Reason);
        await Assert.ThrowsAsync<WsStreamFatalException>(() => session.Completion.WaitAsync(Wait.Limit, Token));

        HandlerEvent stopped = Assert.Single(trades.Events);
        Assert.Equal(HandlerEventKind.Disconnected, stopped.Kind);
        Assert.Equal(WsDisconnectReason.ExchangeFatal, stopped.Reason);
        Assert.True(stopped.IsFatal);

        // A fatal session takes no more requests.
        await Assert.ThrowsAsync<WsStreamFatalException>(async () => await session.SubscribeAsync(_eth, Token));
    }

    [Fact]
    public async Task HandshakeTooManyRequestsBansThenConnects()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        server.Refuse(MarketPath, 429, retryAfterSeconds: 1);
        Stopwatch clock = Stopwatch.StartNew();

        // The ban outlasts the reply wait: the stream stays registered and is subscribed once the ban ends.
        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);
        Assert.Equal(WsStreamSubscribeStatus.Unconfirmed, btc.Status);

        await Wait.UntilAsync(() => route.IsSubscribed(BtcTrades));
        long subscribedAt = clock.ElapsedMilliseconds;
        Assert.True(1000 <= subscribedAt, $"Connected after {subscribedAt} ms.");

        TelemetryEvent failure = Assert.Single(log.Of(TelemetryEventKind.WsStreamConnectFailed));
        Assert.Equal(WsDisconnectReason.HandshakeBanShort, failure.Disconnect.Reason);
        Assert.Equal(429, failure.Disconnect.StatusCode);
        Assert.Equal(1, failure.Disconnect.RetryAfterSeconds);

        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", 1));
        await Wait.UntilAsync(() => trades.Data.Count == 1);
        Assert.Equal(btc.Handle, trades.Data[0].Subscription);
    }

    [Fact]
    public async Task HandshakeIpBanIsFatalAndStopsRest()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        FakeHttpHandler http = Http();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, http: http);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);

        server.Refuse(MarketPath, 418, retryAfterSeconds: 1);

        WsStreamFatalException fatal = await Assert.ThrowsAsync<WsStreamFatalException>(async () => await session.SubscribeAsync(_btc, Token));

        Assert.Equal(WsDisconnectReason.HandshakeBanLong, fatal.Disconnect.Reason);
        Assert.Equal(418, fatal.Disconnect.StatusCode);
        await Assert.ThrowsAsync<WsStreamFatalException>(() => session.Completion.WaitAsync(Wait.Limit, Token));

        HandlerEvent stopped = Assert.Single(trades.Events);
        Assert.Equal(WsDisconnectReason.HandshakeBanLong, stopped.Reason);
        Assert.True(stopped.IsFatal);

        // The IP ban covers REST too: a request waits it out and expires unsent.
        RestApiRequestResult<ServerTimeModel> rest = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(timeToLiveMs: 100, cancellationToken: Token);

        Assert.True(rest.Errors.WasDroppedByTtl);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AgedConnectionRotatesWithoutGap()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, engine: Engine(maxConnectionAge: TimeSpan.FromMilliseconds(200)));
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(MarketPath);

        await session.SubscribeAsync(_btc, Token);

        // Keep the stream busy through two rotations: the old and the new connection both deliver while they overlap.
        long tradeId = 0;
        Stopwatch elapsed = Stopwatch.StartNew();

        while (route.Connections.Count < 3)
        {
            Assert.True(elapsed.Elapsed < Wait.Limit, "No second rotation within the wait limit.");
            await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", ++tradeId));
            await Task.Delay(5, Token);
        }

        long lastId = ++tradeId;
        await route.PushAsync(BtcTrades, FuturesTrade("BTCUSDT", lastId));
        await Wait.UntilAsync(() => trades.Data is { Count: > 0 } data && data[^1].Model.AggregateTradeId == lastId);

        // The handler never heard of the rotations.
        Assert.Empty(trades.Events);

        // Copies from the overlap were dropped: ids only go up.
        IReadOnlyList<StreamRecord<AggTradeModel>> delivered = trades.Data;

        for (int i = 1; i < delivered.Count; i++)
        {
            Assert.True(delivered[i - 1].Model.AggregateTradeId < delivered[i].Model.AggregateTradeId);
        }

        Assert.All(log.Of(TelemetryEventKind.WsStreamDisconnected), item => Assert.Equal(WsDisconnectReason.Rotation, item.Disconnect.Reason));
    }

    [Fact]
    public async Task ServerShutdownRotatesSpotConnection()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, BinanceMarkets.Spot);
        BinanceStreamSession session = client.Spot.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute route = server.Stream(FakeExchangeServer.SpotStreamPath);

        await session.SubscribeAsync(_btc, Token);
        await route.BroadcastAsync(ServerShutdown);

        // Replaced make-before-break: the new connection is subscribed before the old one closes.
        await Wait.UntilAsync(() => route.Connections.Count == 2 && !route.Connections[0].IsOpen && route.IsSubscribed(BtcTrades));

        await route.PushAsync(BtcTrades, SpotTrade("BTCUSDT", 1));
        await Wait.UntilAsync(() => trades.Data.Count == 1);

        Assert.Empty(trades.Events);
        Assert.Contains(log.Of(TelemetryEventKind.WsStreamDisconnected), item => item.Disconnect.Reason == WsDisconnectReason.ServerShutdown);
    }

    /// <summary>Client with streams of the market on the fake exchange. REST and pings go to a fake handler that always answers.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(
        FakeExchangeServer server,
        TelemetryLog log,
        BinanceMarkets streamMarkets = BinanceMarkets.FuturesUsdM,
        EngineOptions? engine = null,
        FakeHttpHandler? http = null) =>
        TestClient.Create(http ?? Http(), log.Handler, Token, TestClient.Options(server, BinanceMarkets.None, streamMarkets), engine ?? TestClient.WsEngine());

    /// <summary>WebSocket test engine with a stream connection age or message ceiling of the test's own.</summary>
    private static EngineOptions Engine(TimeSpan? maxConnectionAge = null, int? maxMessageBytes = null)
    {
        EngineOptions engine = TestClient.WsEngine();

        return engine with
        {
            WsStream = engine.WsStream with
            {
                Network = engine.WsStream.Network with
                {
                    MaxConnectionAge = maxConnectionAge ?? engine.WsStream.Network.MaxConnectionAge,
                    MaxMessageBytes = maxMessageBytes ?? engine.WsStream.Network.MaxMessageBytes,
                },
            },
        };
    }

    private static FakeHttpHandler Http() =>
        new((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Encoding.UTF8.GetString(RestBodies.ServerTime))));

    /// <summary>USD-M aggTrade in the documented key order, with the symbol and trade id of the test.</summary>
    private static string FuturesTrade(string symbol, long tradeId) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""{"e":"aggTrade","E":123456789,"a":{{tradeId}},"s":"{{symbol}}","p":"0.001","q":"100","nq":"100","f":100,"l":105,"T":123456785,"m":true}""");

    /// <summary>Spot aggTrade in the documented key order, with the symbol and trade id of the test.</summary>
    private static string SpotTrade(string symbol, long tradeId) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""{"e":"aggTrade","E":1672515782136,"s":"{{symbol}}","a":{{tradeId}},"p":"0.001","q":"100","f":100,"l":105,"T":1672515782136,"m":true,"M":true}""");
}
