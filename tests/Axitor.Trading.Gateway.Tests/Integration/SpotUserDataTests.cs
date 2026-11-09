using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>Spot user data on its own WebSocket API connection against a local exchange: the hook, gaps, rotation, unsubscribe and fatals.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class SpotUserDataTests
{
    private const string WsApiPath = FakeExchangeServer.SpotWsApiPath;
    private const string SubscribeMethod = "userDataStream.subscribe.signature";
    private const string UnsubscribeMethod = "userDataStream.unsubscribe";
    private const int SlowReconnectMillis = 1000;

    // An event type the session does not know: reported as unknown, which tells the test everything before it was read.
    private const string Marker = """{"e":"testMarker","E":1}""";

    // -2035 SUBSCRIPTION_ACTIVE: User Data Stream subscription already active.
    private const string SubscriptionActiveMessage = "User Data Stream subscription already active.";

    // -2015 REJECTED_MBX_KEY: Invalid API-key, IP, or permissions for action.
    private const string RejectedKeyMessage = "Invalid API-key, IP, or permissions for action.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubscribeWaitsForSessionConnection()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Engine(SlowReconnectMillis));
        FakeWsApiRoute wsApi = WsApi(server);

        // The shared spot lanes are up; the session's own connection is refused once and comes back after the reconnect delay.
        await log.WaitForAsync(TelemetryEventKind.WsApiConnected, 2);
        server.Refuse(WsApiPath, 503);

        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);
        await log.WaitForAsync(TelemetryEventKind.WsApiConnectFailed);

        // No answer while the connection is down: a status given now would say nothing about the subscription.
        Task<WsStreamSubscribeStatus> subscribe = session.SubscribeAsync(Token).AsTask();
        Assert.False(subscribe.IsCompleted);

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await subscribe.WaitAsync(Wait.Limit, Token));

        // One request, sent on the connection that came up.
        FakeConnection connection = Assert.Single(wsApi.Of(SubscribeMethod)).Connection;

        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotExecutionFill));
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotAccountPosition));
        await Wait.UntilAsync(() => userData.Sequence.Count == 2);

        // A fill reaches the handler before the balance change it caused.
        Assert.Equal(["OnExecutionReport", "OnAccountPosition"], userData.Sequence);
        Assert.Empty(userData.Events);

        Assert.Equal(WsStreamSubscribeStatus.AlreadySubscribed, await session.SubscribeAsync(Token));
        Assert.Single(wsApi.Of(SubscribeMethod));
    }

    [Fact]
    public async Task ReconnectClosesGapOnlyAfterNewConnectionIsSubscribed()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        FakeConnection old = await SubscribedAsync(session, wsApi, log);

        wsApi.HoldNext(SubscribeMethod);
        await old.CloseAsync();

        // The new connection is being subscribed by the hook: until the exchange confirms, the gap stays open.
        await Wait.UntilAsync(() => wsApi.Of(SubscribeMethod).Count == 2);

        HandlerEvent disconnected = Assert.Single(userData.Events);
        Assert.Equal(HandlerEventKind.Disconnected, disconnected.Kind);
        Assert.Equal(WsDisconnectReason.ClosedByServer, disconnected.Reason);
        Assert.False(disconnected.IsFatal);

        await wsApi.ReleaseAsync();
        await Wait.UntilAsync(() => userData.Events.Count == 2);

        HandlerEvent reconnected = userData.Events[1];
        Assert.Equal(HandlerEventKind.Reconnected, reconnected.Kind);
        Assert.Equal(WsDisconnectReason.ClosedByServer, reconnected.Gap.Reason);
        Assert.True(reconnected.Gap.StartUtcMillis <= reconnected.Gap.EndUtcMillis);

        FakeConnection fresh = wsApi.Of(SubscribeMethod)[1].Connection;
        Assert.NotSame(old, fresh);

        await fresh.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotExecutionFill));
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
    }

    [Fact]
    public async Task UnexpectedEndResubscribesOnSameConnection()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        FakeConnection connection = await SubscribedAsync(session, wsApi, log);

        // The first resubscribe is applied but its answer is lost; the retry then finds the subscription active.
        wsApi.HoldNext(SubscribeMethod);
        wsApi.FailNext(SubscribeMethod, 400, -2035, SubscriptionActiveMessage);

        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotStreamTerminated));
        await Wait.UntilAsync(() => userData.Events.Count == 2);

        // One gap: the active subscription on our own connection can only be ours, so it closes the gap.
        IReadOnlyList<HandlerEvent> events = userData.Events;
        Assert.Equal(HandlerEventKind.Disconnected, events[0].Kind);
        Assert.Equal(WsDisconnectReason.SubscriptionExpired, events[0].Reason);
        Assert.False(events[0].IsFatal);
        Assert.Equal(HandlerEventKind.Reconnected, events[1].Kind);
        Assert.Equal(WsDisconnectReason.SubscriptionExpired, events[1].Gap.Reason);

        IReadOnlyList<FakeWsApiRequest> subscribes = wsApi.Of(SubscribeMethod);
        Assert.Equal(3, subscribes.Count);
        Assert.All(subscribes, request => Assert.Same(connection, request.Connection));

        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotExecutionFill));
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
    }

    [Fact]
    public async Task ServerShutdownRotatesWithoutGapOrCopies()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        FakeConnection old = await SubscribedAsync(session, wsApi, log);
        await old.SendTextAsync(WsApiFrames.Event(WsApiFrames.ServerShutdown));

        // The replacement is subscribed while the old connection still delivers.
        await Wait.UntilAsync(() => Succeeded(log, SubscribeMethod) == 2);
        FakeConnection fresh = wsApi.Of(SubscribeMethod)[1].Connection;
        Assert.NotSame(old, fresh);

        // The same event on both connections, under different subscription ids: the handler gets it once.
        await old.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotExecutionFill));
        await fresh.SendTextAsync(WsApiFrames.SpotEvent(1, UserDataFrames.SpotExecutionFill));
        await fresh.SendTextAsync(WsApiFrames.SpotEvent(1, UserDataFrames.SpotAccountPosition));
        await Wait.UntilAsync(() => userData.Sequence.Count == 2);

        await Wait.UntilAsync(() => !old.IsOpen);

        Assert.Single(userData.ExecutionReports);
        Assert.Equal(["OnExecutionReport", "OnAccountPosition"], userData.Sequence);
        Assert.Empty(userData.Events);
        Assert.Contains(log.Of(TelemetryEventKind.WsApiDisconnected), item => item.Disconnect.Reason == WsDisconnectReason.ServerShutdown);
    }

    [Fact]
    public async Task UnsubscribeStopsEventsAtOnce()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        FakeConnection connection = await SubscribedAsync(session, wsApi, log);

        Assert.True(await session.UnsubscribeAsync(Token));
        Assert.Same(connection, Assert.Single(wsApi.Of(UnsubscribeMethod)).Connection);

        // A late event and the termination our unsubscribe brings: neither reaches the handler, and no gap opens.
        int unknown = log.Count(TelemetryEventKind.WsApiMessageUnknown);
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotExecutionFill));
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotStreamTerminated));
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, Marker));
        await log.WaitForAsync(TelemetryEventKind.WsApiMessageUnknown, unknown + 1);

        Assert.Empty(userData.Sequence);
        Assert.Empty(userData.Events);

        // The session can be subscribed again.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(Token));
        Assert.Equal(2, wsApi.Of(SubscribeMethod).Count);

        await connection.SendTextAsync(WsApiFrames.SpotEvent(1, UserDataFrames.SpotAccountPosition));
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
    }

    [Fact]
    public async Task RevokedKeyStopsSessionOnce()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        FakeConnection connection = await SubscribedAsync(session, wsApi, log);

        // The API key was deleted while the bot runs: the background resubscribe is refused for good.
        wsApi.FailNext(SubscribeMethod, 401, -2015, RejectedKeyMessage);
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, UserDataFrames.SpotStreamTerminated));

        // Nobody awaits the resubscribe: the handler and Completion are the only way the bot hears of it.
        WsApiFatalException fatal = await Assert.ThrowsAsync<WsApiFatalException>(() => session.Completion.WaitAsync(Wait.Limit, Token));
        Assert.Equal(-2015, fatal.Errors.ExchangeCode);

        IReadOnlyList<HandlerEvent> events = userData.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal(WsDisconnectReason.SubscriptionExpired, events[0].Reason);
        Assert.False(events[0].IsFatal);
        Assert.Equal(HandlerEventKind.Disconnected, events[1].Kind);
        Assert.Equal(WsDisconnectReason.ExchangeFatal, events[1].Reason);
        Assert.True(events[1].IsFatal);

        // A fatal session takes no more requests.
        await Assert.ThrowsAsync<WsApiFatalException>(async () => await session.SubscribeAsync(Token));
        await Assert.ThrowsAsync<WsApiFatalException>(async () => await session.UnsubscribeAsync(Token));
    }

    [Fact]
    public async Task UnansweredSubscribeIsAskedAgainUntilConfirmed()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);

        // Subscribed and unsubscribed first: the connection is past the hook, so the retry below is the call's own.
        FakeConnection connection = await SubscribedAsync(session, wsApi, log);
        Assert.True(await session.UnsubscribeAsync(Token));

        // The next subscribe is never answered: nobody knows whether the exchange applied it.
        wsApi.IgnoreNext(SubscribeMethod);

        // No maybe for the caller: the session asks again and returns once the exchange confirmed.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(Token));

        IReadOnlyList<FakeWsApiRequest> subscribes = wsApi.Of(SubscribeMethod);
        Assert.Equal(3, subscribes.Count);
        Assert.All(subscribes, request => Assert.Same(connection, request.Connection));

        await connection.SendTextAsync(WsApiFrames.SpotEvent(1, UserDataFrames.SpotExecutionFill));
        await Wait.UntilAsync(() => userData.Sequence.Count == 1);
        Assert.Empty(userData.Events);
    }

    [Fact]
    public async Task CanceledSubscribeDeliversNothing()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        SpotUserDataLog userData = new();
        using BinanceClient<TelemetryLogHandler> client = Client(server, log);
        FakeWsApiRoute wsApi = WsApi(server);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(userData.Handler);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        // Subscribed and unsubscribed first: the connection is past the hook, so the request below is the call's own.
        FakeConnection connection = await SubscribedAsync(session, wsApi, log);
        Assert.True(await session.UnsubscribeAsync(Token));

        wsApi.IgnoreNext(SubscribeMethod);
        Task<WsStreamSubscribeStatus> subscribe = session.SubscribeAsync(cancel.Token).AsTask();

        // The caller gives up while the subscribe waits for its answer.
        await Wait.UntilAsync(() => wsApi.Of(SubscribeMethod).Count == 2);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscribe);

        // The exchange may still hold it, yet nothing reaches the handler.
        int unknown = log.Count(TelemetryEventKind.WsApiMessageUnknown);
        await connection.SendTextAsync(WsApiFrames.SpotEvent(1, UserDataFrames.SpotExecutionFill));
        await connection.SendTextAsync(WsApiFrames.SpotEvent(1, Marker));
        await log.WaitForAsync(TelemetryEventKind.WsApiMessageUnknown, unknown + 1);

        Assert.Empty(userData.Sequence);
        Assert.Empty(userData.Events);

        // The session takes a new subscribe.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(Token));
    }

    /// <summary>Client with the spot WebSocket API on the fake exchange. REST and pings go to a fake handler that always answers.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(FakeExchangeServer server, TelemetryLog log, EngineOptions? engine = null) =>
        TestClient.Create(Http(), log.Handler, Token, TestClient.Options(server, BinanceMarkets.Spot, BinanceMarkets.None), engine ?? TestClient.WsEngine());

    /// <summary>WebSocket test engine whose WebSocket API reconnects only after the given delay.</summary>
    private static EngineOptions Engine(int reconnectDelayMillis)
    {
        EngineOptions engine = TestClient.WsEngine();

        return engine with
        {
            WsApi = engine.WsApi with
            {
                Resiliency = engine.WsApi.Resiliency with
                {
                    ReconnectBaseDelayMillis = reconnectDelayMillis,
                    ReconnectMaxDelayMillis = reconnectDelayMillis,
                },
            },
        };
    }

    /// <summary>The spot WebSocket API route, answering subscribe and unsubscribe.</summary>
    private static FakeWsApiRoute WsApi(FakeExchangeServer server)
    {
        FakeWsApiRoute route = server.WsApi(WsApiPath);
        route.Reply(SubscribeMethod, WsApiFrames.SpotSubscription(0));
        route.Reply(UnsubscribeMethod, WsApiFrames.SpotUnsubscribed);
        return route;
    }

    /// <summary>Subscribes and waits until the session is done with the reply. Returns the connection that holds the subscription.</summary>
    private static async Task<FakeConnection> SubscribedAsync(BinanceSpotUserDataSession session, FakeWsApiRoute wsApi, TelemetryLog log)
    {
        // Subscribe waits for the session connection, so the exchange has answered by now.
        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(Token));

        FakeConnection connection = Assert.Single(wsApi.Of(SubscribeMethod)).Connection;

        // One more round trip on the connection: by the time the marker is read, the session is long done with the reply.
        int unknown = log.Count(TelemetryEventKind.WsApiMessageUnknown);
        await connection.SendTextAsync(WsApiFrames.SpotEvent(0, Marker));
        await log.WaitForAsync(TelemetryEventKind.WsApiMessageUnknown, unknown + 1);

        return connection;
    }

    /// <summary>Requests of the method that ended in success.</summary>
    private static int Succeeded(TelemetryLog log, string method)
    {
        int count = 0;

        foreach (TelemetryEvent value in log.Of(TelemetryEventKind.WsApiRequestSucceeded))
        {
            if (value.Method == method)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Answers every REST call and network ping.</summary>
    private static FakeHttpHandler Http() =>
        new((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Encoding.UTF8.GetString(RestBodies.ServerTime))));
}
