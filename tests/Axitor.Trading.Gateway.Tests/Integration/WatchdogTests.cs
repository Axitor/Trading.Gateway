using System.Diagnostics;
using System.Net;
using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>The watchdog stream against a local exchange: a silence checks the network and reconnects every stream; a quiet user stream is left alone.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class WatchdogTests
{
    private const string BtcTrades = "btcusdt@aggTrade";
    private const int WatchdogTimeoutMillis = 300;
    private const int ArmedFrames = 3;

    private static readonly TimeSpan _feedInterval = TimeSpan.FromMilliseconds(20);
    private static readonly Symbol _btc = Symbol.Parse("BTCUSDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SilenceRepairsNetworkAndReconnectsStreams()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        FakeHttpHandler http = Http();
        server.StartWatchdogFeed(_feedInterval);

        using BinanceClient<TelemetryLogHandler> client = Client(server, log, http);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute market = server.Stream(FakeExchangeServer.FuturesMarketPath);
        FakeStreamRoute watchdog = server.Stream(FakeExchangeServer.FuturesPublicPath);

        WsStreamSubscribeResult btc = await session.SubscribeAsync(_btc, Token);

        // The watchdog arms on its first message; then its stream goes quiet while every socket stays open.
        await Wait.UntilAsync(() => ArmedFrames <= server.WatchdogFrames);
        server.PauseWatchdogFeed();

        await log.WaitForAsync(TelemetryEventKind.WatchdogSilent);

        // A stuck node may hold the user connections too: they are dropped and come back subscribed.
        // Each dropped connection reports a network fault, so a second repair may rotate one that already came back.
        await Wait.UntilAsync(() => 2 <= market.Connections.Count && market.IsSubscribed(BtcTrades) && trades.Events.Count == 2);
        await Wait.UntilAsync(() => 2 <= watchdog.Connections.Count && watchdog.IsSubscribed(FakeExchangeServer.WatchdogStream));
        await log.WaitForAsync(TelemetryEventKind.WatchdogResumed);

        TelemetryEvent silent = Assert.Single(log.Of(TelemetryEventKind.WatchdogSilent));
        Assert.True(WatchdogTimeoutMillis <= silent.Number, $"Barked after {silent.Number} ms of silence.");

        // One gap only: later replacements are rotations the handler never hears about.
        IReadOnlyList<HandlerEvent> events = trades.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal(HandlerEventKind.Disconnected, events[0].Kind);
        Assert.Equal(btc.Handle, events[0].Subscription);
        Assert.Equal(WsDisconnectReason.WatchdogTimeout, events[0].Reason);
        Assert.False(events[0].IsFatal);
        Assert.Equal(HandlerEventKind.Reconnected, events[1].Kind);
        Assert.Equal(WsDisconnectReason.WatchdogTimeout, events[1].Gap.Reason);

        Assert.All(log.Of(TelemetryEventKind.WsStreamDisconnected), item => Assert.True(
            item.Disconnect.Reason is WsDisconnectReason.WatchdogTimeout or WsDisconnectReason.NetworkRepaired or WsDisconnectReason.Rotation,
            $"Unexpected drop: {item.Disconnect.Reason}."));

        TelemetryEvent resumed = Assert.Single(log.Of(TelemetryEventKind.WatchdogResumed));
        Assert.Equal(WsDisconnectReason.WatchdogTimeout, resumed.Gap.Reason);

        // Nothing reconnects before a REST ping proves the network is up.
        Assert.NotEmpty(http.Requests);
    }

    [Fact]
    public async Task QuietUserStreamIsNeverDropped()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();
        StreamLog<AggTradeModel> trades = new();
        server.StartWatchdogFeed(_feedInterval);

        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Http());
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(trades.Handler);
        FakeStreamRoute market = server.Stream(FakeExchangeServer.FuturesMarketPath);
        FakeStreamRoute watchdog = server.Stream(FakeExchangeServer.FuturesPublicPath);

        await session.SubscribeAsync(_btc, Token);
        await Wait.UntilAsync(() => ArmedFrames <= server.WatchdogFrames);

        // Three watchdog timeouts without a single trade: a quiet alt stream is normal.
        Stopwatch quiet = Stopwatch.StartNew();
        await Wait.UntilAsync(() => 3 * WatchdogTimeoutMillis <= quiet.ElapsedMilliseconds);

        Assert.Single(market.Connections);
        Assert.Single(watchdog.Connections);
        Assert.Empty(trades.Events);
        Assert.Equal(0, log.Count(TelemetryEventKind.WatchdogSilent));
        Assert.Equal(0, log.Count(TelemetryEventKind.WsStreamDisconnected));
    }

    [Fact]
    public async Task ConnectFailureIsReportedAsWatchdogEvent()
    {
        await using FakeExchangeServer server = new();
        TelemetryLog log = new();

        // A server-side error, e.g. maintenance: retried with back-off.
        server.Refuse(FakeExchangeServer.FuturesPublicPath, 503, count: 2);

        using BinanceClient<TelemetryLogHandler> client = Client(server, log, Http());
        FakeStreamRoute watchdog = server.Stream(FakeExchangeServer.FuturesPublicPath);

        await log.WaitForAsync(TelemetryEventKind.WatchdogConnectFailed, 2);
        await Wait.UntilAsync(() => watchdog.IsSubscribed(FakeExchangeServer.WatchdogStream));

        IReadOnlyList<TelemetryEvent> failures = log.Of(TelemetryEventKind.WatchdogConnectFailed);
        Assert.Equal(2, failures.Count);

        for (int i = 0; i < failures.Count; i++)
        {
            Assert.Equal(i + 1, failures[i].Number);
            Assert.Equal(WsDisconnectReason.HandshakeUnavailable, failures[i].Disconnect.Reason);
            Assert.Equal(503, failures[i].Disconnect.StatusCode);
        }

        // The watchdog keeps to its own events: the regular stream ones stay silent for it.
        Assert.Equal(0, log.Count(TelemetryEventKind.WsStreamConnectFailed));
        Assert.Equal(0, log.Count(TelemetryEventKind.WsStreamConnected));
    }

    private static BinanceClient<TelemetryLogHandler> Client(FakeExchangeServer server, TelemetryLog log, FakeHttpHandler http) =>
        TestClient.Create(http, log.Handler, Token, TestClient.Options(server, BinanceMarkets.None, BinanceMarkets.FuturesUsdM), Engine());

    /// <summary>WebSocket test engine whose watchdog barks after a short silence.</summary>
    private static EngineOptions Engine()
    {
        EngineOptions engine = TestClient.WsEngine();

        return engine with
        {
            WsStream = engine.WsStream with
            {
                Resiliency = engine.WsStream.Resiliency with { WatchdogTimeout = TimeSpan.FromMilliseconds(WatchdogTimeoutMillis) },
            },
        };
    }

    /// <summary>Answers every REST call and network ping.</summary>
    private static FakeHttpHandler Http() =>
        new((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, Encoding.UTF8.GetString(RestBodies.ServerTime))));
}
