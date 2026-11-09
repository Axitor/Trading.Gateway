using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Tests.Fakes.Exchange;

namespace Axitor.Trading.Gateway.Tests.Fakes;

/// <summary>Binance clients for tests: network through a fake handler, no sockets unless asked, delays cut to milliseconds.</summary>
internal static class TestClient
{
    public const string ApiKey = "test-key";
    public const string ApiSecret = "test-secret";

    /// <summary>Options with test keys. Markets left out open no WebSocket connection and start no watchdog.</summary>
    public static BinanceOptions Options(BinanceMarkets wsApiMarkets = BinanceMarkets.None, BinanceMarkets streamMarkets = BinanceMarkets.None) =>
        new(ApiKey, ApiSecret) { WsApiMarkets = wsApiMarkets, StreamMarkets = streamMarkets };

    /// <summary>Options with test keys and every WebSocket route on the fake exchange. REST and pings still go through the fake handler.</summary>
    public static BinanceOptions Options(FakeExchangeServer server, BinanceMarkets wsApiMarkets, BinanceMarkets streamMarkets) =>
        Options(wsApiMarkets, streamMarkets) with
        {
            Uri = new BinanceUri()
            {
                FuturesUsdWebSocketApi = server.UriOf(FakeExchangeServer.FuturesWsApiPath),
                FuturesUsdWebSocketStreamPublic = server.UriOf(FakeExchangeServer.FuturesPublicPath),
                FuturesUsdWebSocketStreamMarket = server.UriOf(FakeExchangeServer.FuturesMarketPath),
                FuturesUsdWebSocketStreamPrivate = server.UriOf(FakeExchangeServer.FuturesPrivatePath),
                FuturesCoinWebSocketApi = server.UriOf(FakeExchangeServer.CoinMWsApiPath),
                FuturesCoinWebSocketStream = server.UriOf(FakeExchangeServer.CoinMStreamPath),
                SpotWebSocketApi = server.UriOf(FakeExchangeServer.SpotWsApiPath),
                SpotWebSocketStream = server.UriOf(FakeExchangeServer.SpotStreamPath),
                SpotSbeStream = server.UriOf(FakeExchangeServer.SpotSbePath),
            },
        };

    /// <summary>Engine options with retry delays of milliseconds and no jitter, so a test never waits seconds.</summary>
    public static EngineOptions Engine()
    {
        EngineOptions defaults = new();

        return defaults with
        {
            RestApi = defaults.RestApi with
            {
                Network = defaults.RestApi.Network with { PingInterval = TimeSpan.FromMilliseconds(10) },
                Resiliency = defaults.RestApi.Resiliency with
                {
                    MaxJitterMillisOnNetworkRepaired = 0,
                    MaxJitterMillisOnRateLimits = 0,
                    MaxJitterMillisOnRetry = 0,
                    MinJitterMillisOnRetry = 0,
                    RetryBaseDelayMillis = 1,
                    RetryMaxDelayMillis = 5,
                    RetryFallbackDelayMillis = 50,
                },
            },
        };
    }

    /// <summary>Engine options for WebSocket tests: reconnects and retries in milliseconds, no jitter, replies awaited for half a second.</summary>
    public static EngineOptions WsEngine()
    {
        EngineOptions engine = Engine();

        return engine with
        {
            WsApi = engine.WsApi with
            {
                Network = engine.WsApi.Network with
                {
                    ResponseTimeout = TimeSpan.FromMilliseconds(500),
                    TimeoutSweepInterval = TimeSpan.FromMilliseconds(20),
                },
                Resiliency = engine.WsApi.Resiliency with
                {
                    ReconnectBaseDelayMillis = 1,
                    ReconnectMaxDelayMillis = 5,
                    MinJitterMillisOnReconnect = 0,
                    MaxJitterMillisOnReconnect = 0,
                    MaxJitterMillisOnNetworkRepaired = 0,
                    ReconnectFallbackDelayMillis = 50,
                    MaxJitterMillisOnRateLimits = 0,
                    MaxJitterMillisOnRetry = 0,
                    MinJitterMillisOnRetry = 0,
                    RetryBaseDelayMillis = 1,
                    RetryMaxDelayMillis = 5,
                    RetryFallbackDelayMillis = 50,
                },
            },
            WsStream = engine.WsStream with
            {
                Network = engine.WsStream.Network with { ResponseTimeout = TimeSpan.FromMilliseconds(500) },
                Resiliency = engine.WsStream.Resiliency with
                {
                    ReconnectBaseDelayMillis = 1,
                    ReconnectMaxDelayMillis = 5,
                    MinJitterMillisOnReconnect = 0,
                    MaxJitterMillisOnReconnect = 0,
                    MaxJitterMillisOnNetworkRepaired = 0,
                    ReconnectFallbackDelayMillis = 50,
                },
            },
        };
    }

    /// <summary>Creates a client over the fake handler. The token stops its background work when the test ends.</summary>
    public static BinanceClient<TTelemetry> Create<TTelemetry>(
        HttpMessageHandler handler,
        TTelemetry telemetry,
        CancellationToken cancellationToken,
        BinanceOptions? options = null,
        EngineOptions? engine = null)
        where TTelemetry : struct, ITelemetryHandler =>
        new(options ?? Options(), engine ?? Engine(), telemetry, handler, cancellationToken);
}
