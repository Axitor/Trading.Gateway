using System.Diagnostics;
using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>The WebSocket API against a local exchange: matching by id, timeouts, bans, lost orders, lanes and rotation after a network repair.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class WsApiTests
{
    private const string TickerMethod = "ticker.price";
    private const string OrderMethod = "order.place";
    private const int SlowReconnectMillis = 1000;
    private const int WatchdogTimeoutMillis = 300;
    private const int ArmedFrames = 3;

    // -1003 TOO_MANY_REQUESTS: Too much request weight used; please use WebSocket Streams for live updates to avoid polling the API.
    private const string TooManyRequestsMessage = "Too much request weight used; please use WebSocket Streams for live updates to avoid polling the API.";

    // -1003 TOO_MANY_REQUESTS: Way too much request weight used; IP banned until %s. Please use WebSocket Streams for live updates to avoid bans.
    private const string IpBannedMessage = "Way too much request weight used; IP banned until 1659142909531. Please use WebSocket Streams for live updates to avoid bans.";

    private static readonly TimeSpan _feedInterval = TimeSpan.FromMilliseconds(20);
    private static readonly Symbol _btc = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _eth = Symbol.Parse("ETHUSDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResponsesOutOfOrderFindTheirRequests()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute route = Route(server);

        route.HoldNext(TickerMethod);
        Task<WsApiRequestResult<TickerPriceModel>> btc = Ticker(client, _btc).AsTask();
        await Wait.UntilAsync(() => route.Of(TickerMethod).Count == 1);

        // The answer to the second request overtakes the first one on the same connection.
        route.Reply(TickerMethod, WsApiFrames.FuturesTickerPrice("ETHUSDT"));
        WsApiRequestResult<TickerPriceModel> eth = await Ticker(client, _eth);

        Assert.Equal(RequestOutcome.Success, eth.Outcome);
        Assert.Equal(_eth, eth.Value.Symbol);
        Assert.False(btc.IsCompleted);

        await route.ReleaseAsync();
        WsApiRequestResult<TickerPriceModel> first = await btc.WaitAsync(Wait.Limit, Token);

        Assert.Equal(RequestOutcome.Success, first.Outcome);
        Assert.Equal(_btc, first.Value.Symbol);
        Assert.NotEqual(first.RequestId, eth.RequestId);

        IReadOnlyList<FakeWsApiRequest> requests = route.Of(TickerMethod);
        Assert.Same(requests[0].Connection, requests[1].Connection);
        Assert.Equal(0, log.Count(TelemetryEventKind.WsApiResponseUnmatched));
    }

    [Fact]
    public async Task TimedOutOrderIsUnknownAndLateAnswerIsUnmatched()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute route = Route(server);

        route.HoldNext(OrderMethod);
        WsApiRequestResult<OrderModel> order = await client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(Order(), cancellationToken: Token);

        // No answer within the response timeout: the order may be live on the exchange, so it is not sent again.
        Assert.Equal(RequestOutcome.Failed, order.Outcome);
        Assert.Equal(WsApiRequestErrorReason.Timeout, order.Errors.Reason);
        Assert.Equal(RequestErrorAction.Skip, order.Resiliency.Action);
        Assert.Equal(0, order.Resiliency.RetryNumber);
        Assert.Single(route.Of(OrderMethod));

        // The answer comes after the request gave up its slot: it is reported, not delivered.
        await route.ReleaseAsync();
        await log.WaitForAsync(TelemetryEventKind.WsApiResponseUnmatched);

        TelemetryEvent unmatched = Assert.Single(log.Of(TelemetryEventKind.WsApiResponseUnmatched));
        Assert.Equal(order.RequestId, unmatched.Number);
    }

    [Fact]
    public async Task TooManyRequestsBansClientForRetryAfter()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        FakeHttpHandler http = Http();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, http: http);
        FakeWsApiRoute route = Route(server);

        route.FailNext(TickerMethod, 429, -1003, TooManyRequestsMessage, retryAfterMillis: 1000);
        Stopwatch clock = Stopwatch.StartNew();
        Task<WsApiRequestResult<TickerPriceModel>> ticker = Ticker(client, _btc).AsTask();

        // The ban covers the whole client: REST waits it out too and expires unsent.
        await log.WaitForAsync(TelemetryEventKind.WsApiRequestRetryAttempt);
        RestApiRequestResult<ServerTimeModel> rest = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(timeToLiveMs: 100, cancellationToken: Token);

        Assert.True(rest.Errors.WasDroppedByTtl);
        Assert.Empty(http.Requests);

        WsApiRequestResult<TickerPriceModel> result = await ticker.WaitAsync(Wait.Limit, Token);
        long retriedAfter = clock.ElapsedMilliseconds;

        // The ban runs from the server time to retryAfter, not by the local clock; the last error stays on the success.
        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Resiliency.RetryNumber);
        Assert.Equal(WsApiRequestErrorReason.ExchangeApiBanShort, result.Errors.Reason);
        Assert.Equal(429, result.Errors.StatusCode);
        Assert.Equal(-1003, result.Errors.ExchangeCode);
        Assert.Equal(1000, result.Errors.RetryAfterMillis);
        Assert.True(1000 <= retriedAfter, $"Retried after {retriedAfter} ms.");
        Assert.Equal(2, route.Of(TickerMethod).Count);
    }

    [Fact]
    public async Task IpBanIsFatalAndStopsRest()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        FakeHttpHandler http = Http();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, http: http);
        FakeWsApiRoute route = Route(server);

        await log.WaitForAsync(TelemetryEventKind.WsApiConnected, 2);
        route.FailNext(TickerMethod, 418, -1003, IpBannedMessage, retryAfterMillis: 2000);

        WsApiFatalException fatal = await Assert.ThrowsAsync<WsApiFatalException>(async () => await Ticker(client, _btc));

        Assert.Equal(WsApiRequestErrorReason.ExchangeApiBanLong, fatal.Errors.Reason);
        Assert.Equal(418, fatal.Errors.StatusCode);
        Assert.Equal(-1003, fatal.Errors.ExchangeCode);
        Assert.Equal(2000, fatal.Errors.RetryAfterMillis);
        Assert.Equal(RequestErrorAction.Fatal, fatal.Resiliency.Action);

        // Every request would extend the ban: both lanes close and nothing is retried.
        await Wait.UntilAsync(() => AllClosed(route));
        Assert.Single(route.Of(TickerMethod));

        // The IP ban covers REST too: a request waits it out and expires unsent.
        RestApiRequestResult<ServerTimeModel> rest = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(timeToLiveMs: 100, cancellationToken: Token);

        Assert.True(rest.Errors.WasDroppedByTtl);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task OrderLostWithConnectionIsNotResent()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute route = Route(server);

        route.IgnoreNext(OrderMethod);
        Task<WsApiRequestResult<OrderModel>> pending = client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(Order(), cancellationToken: Token).AsTask();

        await Wait.UntilAsync(() => route.Of(OrderMethod).Count == 1);
        route.Of(OrderMethod)[0].Connection.Abort();

        WsApiRequestResult<OrderModel> lost = await pending.WaitAsync(Wait.Limit, Token);

        // The order reached the exchange and may be live: without isRetryable it never goes out again.
        Assert.Equal(RequestOutcome.Failed, lost.Outcome);
        Assert.Equal(WsApiRequestErrorReason.ConnectionLost, lost.Errors.Reason);
        Assert.Equal(RequestErrorAction.Skip, lost.Resiliency.Action);
        Assert.Equal(0, lost.Resiliency.RetryNumber);
        Assert.Single(route.Of(OrderMethod));

        // The lane recovers on its own and the next order goes out once.
        WsApiRequestResult<OrderModel> next = await client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(Order(), cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, next.Outcome);
        Assert.Equal(2, route.Of(OrderMethod).Count);
    }

    [Fact]
    public async Task FullInFlightTableRejectsAtOnce()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Engine(maxInFlight: 2));
        FakeWsApiRoute route = Route(server);

        await LanesAsync(client, route);
        int sent = route.Of(TickerMethod).Count;

        route.HoldNext(TickerMethod);
        route.HoldNext(TickerMethod);
        Task<WsApiRequestResult<TickerPriceModel>> first = Ticker(client, _btc).AsTask();
        Task<WsApiRequestResult<TickerPriceModel>> second = Ticker(client, _btc).AsTask();
        await Wait.UntilAsync(() => route.Of(TickerMethod).Count == sent + 2);

        // Every Normal slot waits on a lagging exchange: one more request is refused at once rather than queued behind them.
        WsApiRequestResult<TickerPriceModel> rejected = await Ticker(client, _btc);

        Assert.Equal(RequestOutcome.Failed, rejected.Outcome);
        Assert.Equal(WsApiRequestErrorReason.InFlightLimit, rejected.Errors.Reason);
        Assert.Equal(RequestErrorAction.Skip, rejected.Resiliency.Action);
        Assert.Equal(0, rejected.RequestId);
        Assert.Equal(sent + 2, route.Of(TickerMethod).Count);

        // The Critical lane has slots of its own.
        WsApiRequestResult<TickerPriceModel> critical = await Ticker(client, _btc, RequestPriority.Critical);
        Assert.Equal(RequestOutcome.Success, critical.Outcome);

        await route.ReleaseAsync();

        Assert.Equal(RequestOutcome.Success, (await first.WaitAsync(Wait.Limit, Token)).Outcome);
        Assert.Equal(RequestOutcome.Success, (await second.WaitAsync(Wait.Limit, Token)).Outcome);
    }

    [Fact]
    public async Task RequestWaitsForConnectionWithinTtl()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Engine(reconnectDelayMillis: SlowReconnectMillis));
        FakeWsApiRoute route = Route(server);

        (FakeConnection critical, FakeConnection normal) = await LanesAsync(client, route);
        int sent = route.Of(TickerMethod).Count;

        await normal.CloseAsync();
        await Wait.UntilAsync(() => IsLaneDown(log, RequestPriority.Normal));

        // The Normal lane comes back only after the reconnect delay: a short TTL runs out unsent, and the open Critical lane is not borrowed.
        WsApiRequestResult<TickerPriceModel> expired = await Ticker(client, _btc, timeToLiveMs: 100);

        Assert.Equal(RequestOutcome.Failed, expired.Outcome);
        Assert.True(expired.Errors.WasDroppedByTtl);
        Assert.Equal(0, expired.RequestId);
        Assert.Equal(sent, route.Of(TickerMethod).Count);

        // A TTL that outlasts the reconnect waits for the new connection; waiting is not a retry.
        WsApiRequestResult<TickerPriceModel> waited = await Ticker(client, _btc, timeToLiveMs: 5000);

        Assert.Equal(RequestOutcome.Success, waited.Outcome);
        Assert.Equal(0, waited.Resiliency.RetryNumber);

        FakeConnection landed = route.Of(TickerMethod)[^1].Connection;
        Assert.NotSame(normal, landed);
        Assert.NotSame(critical, landed);
    }

    [Fact]
    public async Task CriticalTakesNormalLaneWhileItsOwnReconnects()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Engine(reconnectDelayMillis: SlowReconnectMillis));
        FakeWsApiRoute route = Route(server);

        (FakeConnection critical, FakeConnection normal) = await LanesAsync(client, route);

        await critical.CloseAsync();
        await Wait.UntilAsync(() => IsLaneDown(log, RequestPriority.Critical));

        // An order does not wait out the reconnect delay while the Normal lane is up.
        Stopwatch clock = Stopwatch.StartNew();
        WsApiRequestResult<OrderModel> order = await client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(Order(), cancellationToken: Token);
        long placedAfter = clock.ElapsedMilliseconds;

        Assert.Equal(RequestOutcome.Success, order.Outcome);
        Assert.Same(normal, route.Of(OrderMethod)[^1].Connection);
        Assert.True(placedAfter < SlowReconnectMillis, $"Placed after {placedAfter} ms.");

        // Once its own lane is back, Critical leaves the Normal one.
        await log.WaitForAsync(TelemetryEventKind.WsApiConnected, 3);
        FakeConnection back = await CriticalLaneAsync(client, route, normal);

        Assert.NotSame(critical, back);
    }

    [Fact]
    public async Task WatchdogSilenceRotatesWsApiMakeBeforeBreak()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        FakeHttpHandler http = Http();
        server.StartWatchdogFeed(_feedInterval);

        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Engine(watchdogTimeout: TimeSpan.FromMilliseconds(WatchdogTimeoutMillis)), http);
        FakeWsApiRoute route = Route(server);

        await log.WaitForAsync(TelemetryEventKind.WsApiConnected, 2);
        await Wait.UntilAsync(() => ArmedFrames <= server.WatchdogFrames);
        server.PauseWatchdogFeed();
        await log.WaitForAsync(TelemetryEventKind.WatchdogSilent);

        // The bark leaves WS API sockets open; after the repair every lane gets a new connection, and only then the old one closes.
        await Wait.UntilAsync(() => FirstTwoReplaced(route) && 2 <= log.Count(TelemetryEventKind.WsApiDisconnected));

        IReadOnlyList<TelemetryEvent> events = log.Events;

        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Kind != TelemetryEventKind.WsApiDisconnected)
            {
                continue;
            }

            Assert.Equal(WsDisconnectReason.NetworkRepaired, events[i].Disconnect.Reason);
            Assert.True(OpenedReplacementFirst(events, i), $"Connection {events[i].WsApiConnection.Epoch} closed before its replacement opened.");
        }

        // Nothing is replaced before a REST ping proves the network is up.
        Assert.NotEmpty(http.Requests);

        // Requests go on the new connections.
        FakeConnection landed = await ProbeAsync(client, route, RequestPriority.Normal);
        IReadOnlyList<FakeConnection> connections = route.Connections;

        Assert.NotSame(connections[0], landed);
        Assert.NotSame(connections[1], landed);
    }

    /// <summary>Client with the USD-M WebSocket API on the fake exchange. REST and pings go to a fake handler that always answers.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(FakeExchangeServer server, TelemetryLog log, EngineOptions? engine = null, FakeHttpHandler? http = null) =>
        TestClient.Create(http ?? Http(), log.Handler, Token, TestClient.Options(server, BinanceMarkets.FuturesUsdM, BinanceMarkets.None), engine ?? TestClient.WsEngine());

    /// <summary>WebSocket test engine with an in-flight table, a reconnect delay or a watchdog timeout of the test's own.</summary>
    private static EngineOptions Engine(int? maxInFlight = null, int? reconnectDelayMillis = null, TimeSpan? watchdogTimeout = null)
    {
        EngineOptions engine = TestClient.WsEngine();

        return engine with
        {
            WsApi = engine.WsApi with
            {
                Network = engine.WsApi.Network with { MaxInFlight = maxInFlight ?? engine.WsApi.Network.MaxInFlight },
                Resiliency = engine.WsApi.Resiliency with
                {
                    ReconnectBaseDelayMillis = reconnectDelayMillis ?? engine.WsApi.Resiliency.ReconnectBaseDelayMillis,
                    ReconnectMaxDelayMillis = reconnectDelayMillis ?? engine.WsApi.Resiliency.ReconnectMaxDelayMillis,
                },
            },
            WsStream = engine.WsStream with
            {
                Resiliency = engine.WsStream.Resiliency with { WatchdogTimeout = watchdogTimeout ?? engine.WsStream.Resiliency.WatchdogTimeout },
            },
        };
    }

    /// <summary>The USD-M WebSocket API route, answering ticker.price for BTCUSDT and order.place with the documented order.</summary>
    private static FakeWsApiRoute Route(FakeExchangeServer server)
    {
        FakeWsApiRoute route = server.WsApi(FakeExchangeServer.FuturesWsApiPath);
        route.Reply(TickerMethod, WsApiFrames.FuturesTickerPrice("BTCUSDT"));
        route.Reply(OrderMethod, WsApiFrames.FuturesOrder);
        return route;
    }

    /// <summary>Server ends of the Critical and the Normal lane, found by where probes land.</summary>
    private static async Task<(FakeConnection Critical, FakeConnection Normal)> LanesAsync(BinanceClient<TelemetryLogHandler> client, FakeWsApiRoute route)
    {
        FakeConnection normal = await ProbeAsync(client, route, RequestPriority.Normal);
        FakeConnection critical = await CriticalLaneAsync(client, route, normal);

        return (critical, normal);
    }

    /// <summary>Where Critical probes land once the Critical lane is current. Until then they take the Normal lane.</summary>
    private static async Task<FakeConnection> CriticalLaneAsync(BinanceClient<TelemetryLogHandler> client, FakeWsApiRoute route, FakeConnection normal)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (true)
        {
            FakeConnection landed = await ProbeAsync(client, route, RequestPriority.Critical);

            if (!ReferenceEquals(landed, normal))
            {
                return landed;
            }

            Assert.True(elapsed.Elapsed < Wait.Limit, "The Critical lane did not come up within the wait limit.");
            await Task.Delay(5, Token);
        }
    }

    /// <summary>Sends a ticker request of the priority and returns the server connection it came on.</summary>
    private static async Task<FakeConnection> ProbeAsync(BinanceClient<TelemetryLogHandler> client, FakeWsApiRoute route, RequestPriority priority)
    {
        WsApiRequestResult<TickerPriceModel> probe = await Ticker(client, _btc, priority);

        Assert.Equal(RequestOutcome.Success, probe.Outcome);
        return route.Of(TickerMethod)[^1].Connection;
    }

    private static ValueTask<WsApiRequestResult<TickerPriceModel>> Ticker(
        BinanceClient<TelemetryLogHandler> client,
        Symbol symbol,
        RequestPriority priority = RequestPriority.Normal,
        int timeToLiveMs = 0) =>
        client.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(symbol, timeToLiveMs: timeToLiveMs, priority: priority, cancellationToken: Token);

    /// <summary>Whether the client has seen a connection of the lane drop.</summary>
    private static bool IsLaneDown(TelemetryLog log, RequestPriority lane)
    {
        foreach (TelemetryEvent value in log.Of(TelemetryEventKind.WsApiDisconnected))
        {
            if (value.WsApiConnection.Lane == lane)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Both lanes were up and every connection of the route has ended.</summary>
    private static bool AllClosed(FakeWsApiRoute route)
    {
        IReadOnlyList<FakeConnection> connections = route.Connections;

        if (connections.Count < 2)
        {
            return false;
        }

        foreach (FakeConnection connection in connections)
        {
            if (connection.IsOpen)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The first two connections have closed and at least two replacements came.</summary>
    private static bool FirstTwoReplaced(FakeWsApiRoute route)
    {
        IReadOnlyList<FakeConnection> connections = route.Connections;
        return 4 <= connections.Count && !connections[0].IsOpen && !connections[1].IsOpen;
    }

    /// <summary>Whether a newer connection of the same lane opened before the drop at the index.</summary>
    private static bool OpenedReplacementFirst(IReadOnlyList<TelemetryEvent> events, int droppedAt)
    {
        WsApiConnectionInfo dropped = events[droppedAt].WsApiConnection;

        for (int i = 0; i < droppedAt; i++)
        {
            TelemetryEvent value = events[i];

            if (value.Kind == TelemetryEventKind.WsApiConnected && value.WsApiConnection.Lane == dropped.Lane && dropped.Epoch < value.WsApiConnection.Epoch)
            {
                return true;
            }
        }

        return false;
    }

    private static NewOrderRequest Order() => new()
    {
        Symbol = _btc,
        ClientOrderId = ClientOrderId.Parse("my-order-1"),
        Filters = default,
        Side = OrderSide.BUY,
        Type = OrderType.LIMIT,
        TimeInForce = TimeInForce.GTC,
        Quantity = 0.01,
        Price = 60000,
    };

    /// <summary>Answers every REST call and network ping.</summary>
    private static FakeHttpHandler Http() =>
        new((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Encoding.UTF8.GetString(RestBodies.ServerTime))));
}
