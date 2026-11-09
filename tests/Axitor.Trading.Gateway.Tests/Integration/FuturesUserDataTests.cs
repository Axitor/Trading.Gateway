using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>Futures user data against a local exchange: per-event subscriptions on the key, key expiry and checks, gaps reported once, fatals.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class FuturesUserDataTests
{
    private const string PrivatePath = FakeExchangeServer.FuturesPrivatePath;
    private const string StartMethod = "userDataStream.start";
    private const string Key = "pqia91ma19a5s61cv6a81va65sdf19v8a65a1a5s61cv6a81va65sdf19v8a65a1";
    private const string NewKey = "xt8Ql8B1nI3c0fT7mWv2Hn5aYbK4sRzE9uJdLp6gQoVhXy1CwMe0iFkA7tNjUr3S";
    private const string AnchorSuffix = "@listenKeyExpired";
    private const string OrderSuffix = "@ORDER_TRADE_UPDATE";
    private const string AccountSuffix = "@ACCOUNT_UPDATE";
    private const int KeepAliveMillis = 50;
    private const BinanceUserDataEvents OrdersAndAccount = BinanceUserDataEvents.OrderTradeUpdate | BinanceUserDataEvents.AccountUpdate;

    // -2015 REJECTED_MBX_KEY: Invalid API-key, IP, or permissions for action.
    private const string RejectedKeyMessage = "Invalid API-key, IP, or permissions for action.";

    // -1102 MANDATORY_PARAM_EMPTY_OR_MALFORMED: A mandatory parameter was not sent, was empty/null, or malformed.
    private const string MalformedMessage = "A mandatory parameter was not sent, was empty/null, or malformed.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubscribesEachEventOnKeyAndKeepsExchangeOrder()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        WsStreamSubscribeStatus status = await session.SubscribeAsync(OrdersAndAccount, Token);

        // Only the asked events plus the expiry anchor: a bare key would send every event twice.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, status);
        Assert.True(stream.IsSubscribed(Key + AnchorSuffix));
        Assert.True(stream.IsSubscribed(Key + OrderSuffix));
        Assert.True(stream.IsSubscribed(Key + AccountSuffix));
        Assert.False(stream.IsSubscribed(Key));
        Assert.False(stream.IsSubscribed(Key + "@TRADE_LITE"));
        Assert.Single(wsApi.Of(StartMethod));

        // One connection for all events: a fill reaches the handler before the position change it caused.
        await stream.PushAsync(Key + OrderSuffix, UserDataFrames.OrderTradeUpdate);
        await stream.PushAsync(Key + AccountSuffix, UserDataFrames.AccountUpdate);
        await Wait.UntilAsync(() => userData.Sequence.Count == 2);

        Assert.Equal(["OnOrderUpdate", "OnAccountUpdate"], userData.Sequence);
        Assert.Single(stream.Connections);
        Assert.Empty(userData.Events);

        // One subscription per session.
        Assert.Equal(WsStreamSubscribeStatus.AlreadySubscribed, await session.SubscribeAsync(OrdersAndAccount, Token));
        Assert.Single(wsApi.Of(StartMethod));
    }

    [Fact]
    public async Task ExpiredKeyMovesToNewKeyWithOneGap()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        await session.SubscribeAsync(OrdersAndAccount, Token);

        // The key died, e.g. another app on the account closed it: the exchange now hands out a new one.
        wsApi.Reply(StartMethod, WsApiFrames.ListenKey(NewKey));
        await stream.PushAsync(Key + AnchorSuffix, UserDataFrames.ListenKeyExpired(Key));

        await Wait.UntilAsync(() => userData.Events.Count == 2 && stream.IsSubscribed(NewKey + AnchorSuffix) && stream.IsSubscribed(NewKey + AccountSuffix));

        IReadOnlyList<HandlerEvent> events = userData.Events;
        Assert.Equal(HandlerEventKind.Disconnected, events[0].Kind);
        Assert.Equal(WsDisconnectReason.SubscriptionExpired, events[0].Reason);
        Assert.False(events[0].IsFatal);
        Assert.Equal(HandlerEventKind.Reconnected, events[1].Kind);
        Assert.Equal(WsDisconnectReason.SubscriptionExpired, events[1].Gap.Reason);
        Assert.True(events[1].Gap.StartUtcMillis <= events[1].Gap.EndUtcMillis);

        Assert.True(stream.IsSubscribed(NewKey + OrderSuffix));
        Assert.False(stream.IsSubscribed(Key + AnchorSuffix));
        Assert.False(stream.IsSubscribed(Key + OrderSuffix));
        Assert.False(stream.IsSubscribed(Key + AccountSuffix));

        await stream.PushAsync(NewKey + OrderSuffix, UserDataFrames.OrderTradeUpdate);
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconnectClosesGapOnlyAfterKeyCheck(bool keyDied)
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        await session.SubscribeAsync(OrdersAndAccount, Token);

        // A slow answer makes the session request the key again, so the count goes from here.
        int starts = wsApi.Of(StartMethod).Count;

        // A key that died while the connection was down: its expiry event was lost with the connection.
        if (keyDied)
        {
            wsApi.Reply(StartMethod, WsApiFrames.ListenKey(NewKey));
        }

        string current = keyDied ? NewKey : Key;

        wsApi.HoldNext(StartMethod);
        await stream.Connections[0].CloseAsync();

        // The new connection subscribes the old names again, yet the gap stays open while the key check is pending.
        await Wait.UntilAsync(() => wsApi.Of(StartMethod).Count == starts + 1);

        HandlerEvent disconnected = Assert.Single(userData.Events);
        Assert.Equal(HandlerEventKind.Disconnected, disconnected.Kind);
        Assert.Equal(WsDisconnectReason.ClosedByServer, disconnected.Reason);
        Assert.False(disconnected.IsFatal);

        await wsApi.ReleaseAsync();
        await Wait.UntilAsync(() => userData.Events.Count == 2 && stream.IsSubscribed(current + AnchorSuffix) && stream.IsSubscribed(current + OrderSuffix));

        // One gap with its real cause, whether or not the key changed.
        HandlerEvent reconnected = userData.Events[1];
        Assert.Equal(HandlerEventKind.Reconnected, reconnected.Kind);
        Assert.Equal(WsDisconnectReason.ClosedByServer, reconnected.Gap.Reason);

        // A dead key's names are dropped; a live one keeps them.
        Assert.Equal(!keyDied, stream.IsSubscribed(Key + OrderSuffix));

        await stream.PushAsync(current + OrderSuffix, UserDataFrames.OrderTradeUpdate);
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
    }

    [Fact]
    public async Task KeepAliveExtendsKeyWithoutGap()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, TimeSpan.FromMilliseconds(KeepAliveMillis));
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        await session.SubscribeAsync(OrdersAndAccount, Token);
        FakeConnection connection = Assert.Single(stream.Connections);
        int controls = connection.Received.Count;

        // The same key comes back extended: nothing is resubscribed and the handler hears nothing.
        await Wait.UntilAsync(() => 4 <= wsApi.Of(StartMethod).Count);

        Assert.Equal(controls, connection.Received.Count);
        Assert.Single(stream.Connections);
        Assert.True(stream.IsSubscribed(Key + OrderSuffix));
        Assert.Empty(userData.Events);
    }

    [Fact]
    public async Task RefusedKeyRequestSubscribesNothing()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        wsApi.FailNext(StartMethod, 400, -1102, MalformedMessage);

        // No key, no subscriptions: nothing is left half-subscribed on a key the session does not hold.
        Assert.Equal(WsStreamSubscribeStatus.Rejected, await session.SubscribeAsync(OrdersAndAccount, Token));
        Assert.False(stream.IsSubscribed(Key + AnchorSuffix));
        Assert.False(stream.IsSubscribed(Key + OrderSuffix));
        Assert.Empty(userData.Events);

        // The session stays usable.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(OrdersAndAccount, Token));
        Assert.True(stream.IsSubscribed(Key + OrderSuffix));
    }

    [Fact]
    public async Task FatalKeyCheckStopsSessionOnce()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, TimeSpan.FromMilliseconds(KeepAliveMillis));
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        await session.SubscribeAsync(OrdersAndAccount, Token);

        // The API key was deleted while the bot runs: the next keep-alive is refused for good.
        wsApi.FailNext(StartMethod, 401, -2015, RejectedKeyMessage);

        // Nobody awaits the key loop: the handler and Completion are the only way the bot hears of it.
        WsStreamFatalException fatal = await Assert.ThrowsAsync<WsStreamFatalException>(() => session.Completion.WaitAsync(Wait.Limit, Token));

        Assert.Equal(WsDisconnectReason.ExchangeFatal, fatal.Disconnect.Reason);
        WsApiFatalException cause = Assert.IsType<WsApiFatalException>(fatal.InnerException);
        Assert.Equal(-2015, cause.Errors.ExchangeCode);

        HandlerEvent stopped = Assert.Single(userData.Events);
        Assert.Equal(HandlerEventKind.Disconnected, stopped.Kind);
        Assert.Equal(WsDisconnectReason.ExchangeFatal, stopped.Reason);
        Assert.True(stopped.IsFatal);

        await Wait.UntilAsync(() => !stream.Connections[0].IsOpen);
    }

    [Theory]
    [InlineData(BinanceUserDataEvents.TradeLite)]
    [InlineData(BinanceUserDataEvents.AlgoUpdate)]
    public async Task CoinMRefusesUsdMOnlyEvents(BinanceUserDataEvents events)
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, market: BinanceMarkets.FuturesCoinM);
        BinanceUserDataSession session = client.Futures.CoinM.WsStreams.UserData.CreateSession(userData.Handler);

        // COIN-M takes the subscription but never sends these events: the session would stay silent for good.
        await Assert.ThrowsAsync<ArgumentException>(async () => await session.SubscribeAsync(BinanceUserDataEvents.OrderTradeUpdate | events, Token));

        Assert.Empty(server.WsApi(FakeExchangeServer.CoinMWsApiPath).Of(StartMethod));
    }

    [Fact]
    public async Task UnansweredSubscribeStartsOverUntilConfirmed()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);

        // The anchor is applied but never answered: nobody knows whether the exchange holds it.
        stream.IgnoreNext();

        // No maybe for the caller: the session starts over and returns once every subscription is confirmed.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(OrdersAndAccount, Token));

        // Each round takes the key again. A slow runner can miss one more reply and add a round: the same behavior, one round later.
        Assert.True(2 <= wsApi.Of(StartMethod).Count);
        Assert.True(stream.IsSubscribed(Key + AnchorSuffix));
        Assert.True(stream.IsSubscribed(Key + OrderSuffix));
        Assert.True(stream.IsSubscribed(Key + AccountSuffix));

        await stream.PushAsync(Key + OrderSuffix, UserDataFrames.OrderTradeUpdate);
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
        Assert.Empty(userData.Events);
    }

    [Fact]
    public async Task CanceledSubscribeLeavesNothingSubscribed()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        UserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        FakeStreamRoute stream = server.Stream(PrivatePath);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(userData.Handler);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        stream.IgnoreNext();
        Task<WsStreamSubscribeStatus> subscribe = session.SubscribeAsync(OrdersAndAccount, cancel.Token).AsTask();

        // The caller gives up while the anchor waits for its answer.
        await Wait.UntilAsync(() => stream.IsSubscribed(Key + AnchorSuffix));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscribe);

        // Undone: nothing of the canceled call stays on the exchange, and the session takes a new subscribe.
        Assert.False(stream.IsSubscribed(Key + AnchorSuffix));
        Assert.False(stream.IsSubscribed(Key + OrderSuffix));
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(OrdersAndAccount, Token));
        Assert.True(stream.IsSubscribed(Key + OrderSuffix));
    }

    /// <summary>Client with user data of the market on the fake exchange. REST and pings go to a fake handler that always answers.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(
        FakeExchangeServer server,
        TelemetryLog log,
        TimeSpan? keepAliveInterval = null,
        BinanceMarkets market = BinanceMarkets.FuturesUsdM)
    {
        BinanceOptions options = TestClient.Options(server, market, market);
        options = options with { UserDataKeepAliveInterval = keepAliveInterval ?? options.UserDataKeepAliveInterval };

        // No test here waits for a WebSocket API timeout. A long one keeps a slow CI runner from retrying a key request and counting an extra start.
        EngineOptions engine = TestClient.WsEngine();
        engine = engine with { WsApi = engine.WsApi with { Network = engine.WsApi.Network with { ResponseTimeout = TimeSpan.FromSeconds(5) } } };

        return TestClient.Create(Http(), log.Handler, Token, options, engine);
    }

    /// <summary>The USD-M WebSocket API route, handing out the test key.</summary>
    private static FakeWsApiRoute WsApi(FakeExchangeServer server)
    {
        FakeWsApiRoute route = server.WsApi(FakeExchangeServer.FuturesWsApiPath);
        route.Reply(StartMethod, WsApiFrames.ListenKey(Key));
        return route;
    }

    /// <summary>Answers every REST call and network ping.</summary>
    private static FakeHttpHandler Http() =>
        new((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Encoding.UTF8.GetString(RestBodies.ServerTime))));
}
