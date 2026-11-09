using System.Text;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Binance.Net;
using Binance.Net.Clients;
using CryptoExchange.Net.Objects;

namespace Axitor.Trading.Gateway.Benchmarks.Clients;

/// <summary>Our client and Binance.Net, both pointed at the exchange stub with client-side rate limits off.</summary>
public sealed class BenchmarkClients : IDisposable
{
    /// <summary>Symbol every REST benchmark asks for; the stub has its order book recorded.</summary>
    public const string SymbolName = "BTCUSDT";

    /// <summary>Watchdog stream with no recording: the watchdog never arms, so it never drops the streams after the stub's pass.</summary>
    public const string WatchdogStreamName = "axitor@benchwatchdog";

    // Signed calls need a key and a secret; the stub checks neither.
    private const string ApiKey = "stub-key";
    private const string ApiSecret = "stub-secret";

    // Subscribes go out one per symbol; Binance.Net sends its own with the limiter off.
    private const int StreamControlMessagesPerSecond = 1000;

    /// <summary>Host and port of the exchange stub. AXITOR_STUB_HOST overrides it.</summary>
    public static readonly string StubHost = Environment.GetEnvironmentVariable("AXITOR_STUB_HOST") ?? "127.0.0.1:5100";

    public static readonly Symbol Symbol = Symbol.Parse(SymbolName);

    /// <summary>Symbols the recorder took the streams of.</summary>
    public static readonly string[] StreamSymbolNames =
    [
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "BNBUSDT", "ADAUSDT", "LINKUSDT", "AVAXUSDT", "SUIUSDT",
        "LTCUSDT", "DOTUSDT", "BCHUSDT", "TRXUSDT", "NEARUSDT", "APTUSDT", "ARBUSDT", "OPUSDT", "FILUSDT", "ETCUSDT",
    ];

    private static readonly string _http = "http://" + StubHost;
    private static readonly string _ws = "ws://" + StubHost;

    public BenchmarkClients()
    {
        // REST only: no WebSocket API, no streams, no watchdog.
        Axitor = new BinanceClient<NoOpTelemetryHandler>(CreateAxitorOptions(BinanceMarkets.None), new EngineOptions());
        BinanceNet = CreateBinanceNetRestClient();
    }

    public BinanceClient<NoOpTelemetryHandler> Axitor { get; }

    public BinanceRestClient BinanceNet { get; }

    /// <summary>Our options against the stub with client-side limits out of the way.</summary>
    public static BinanceOptions CreateAxitorOptions(BinanceMarkets streamMarkets, BinanceMarkets wsApiMarkets = BinanceMarkets.None) =>
        new(ApiKey, ApiSecret)
        {
            Uri = new BinanceUri()
            {
                PingURI = new Uri(_http + "/fapi/v1/time"),
                WatchdogStream = Encoding.UTF8.GetBytes(WatchdogStreamName),
                FuturesUsdRestApi = Encoding.UTF8.GetBytes(_http),
                FuturesUsdWebSocketApi = new Uri(_ws + "/ws-fapi/v1"),
                FuturesUsdWebSocketStreamPublic = new Uri(_ws + "/public/stream"),
                FuturesUsdWebSocketStreamMarket = new Uri(_ws + "/market/stream"),
                FuturesUsdWebSocketStreamPrivate = new Uri(_ws + "/private/stream"),
            },
            Limits = new BinanceLimits()
            {
                // The limiter multiplies the weight limit by the snapshot percent in int: the product must stay in range.
                FuturesRestApiLimitRequestWeightPerMinute = int.MaxValue / 100,
                FuturesDepthSnapshotWeightPercent = 100,
                FuturesRestApiLimitOrderWeightPerMinute = int.MaxValue,
                FuturesRestApiLimitOrderWeightPer10s = int.MaxValue,

                // Its own window, so a long run never drains the REST one.
                FuturesWsApiLimitRequestWeightPerMinute = int.MaxValue,
                FuturesWsApiSharesRestRequestWeight = false,

                FuturesWebsocketStreamLimitMessageAmountPerSecond = StreamControlMessagesPerSecond,
            },
            WsApiMarkets = wsApiMarkets,
            StreamMarkets = streamMarkets,
        };

    /// <summary>Binance.Net environment with every USD-M address on the stub. Binance.Net adds the WebSocket API paths itself.</summary>
    public static BinanceEnvironment CreateBinanceNetEnvironment() =>
        BinanceEnvironment.CreateCustom(
            "stub",
            _http,
            _ws + "/",
            _ws + "/",
            null,
            _http,
            _ws + "/",
            _ws + "/",
            null,
            null,
            null);

    /// <summary>Binance.Net REST client against the stub: no limiter, no server time sync, as ours.</summary>
    public static BinanceRestClient CreateBinanceNetRestClient() =>
        new(settings =>
        {
            settings.Environment = CreateBinanceNetEnvironment();
            settings.ApiCredentials = new BinanceCredentials(ApiKey, ApiSecret);
            settings.RateLimiterEnabled = false;
            settings.AutoTimestamp = false;
        });

    /// <summary>Binance.Net stream client against the stub: no limiter and no reconnect, as in its own benchmarks.</summary>
    public static BinanceSocketClient CreateBinanceNetSocketClient() =>
        new(settings =>
        {
            settings.Environment = CreateBinanceNetEnvironment();
            settings.RateLimiterEnabled = false;

            // A reconnect would get a second pass of the recording.
            settings.ReconnectPolicy = ReconnectPolicy.Disabled;
        });

    /// <summary>Binance.Net WebSocket API client against the stub: signed, no limiter, client order ids sent as given.</summary>
    public static BinanceSocketClient CreateBinanceNetWsApiClient() =>
        new(settings =>
        {
            settings.Environment = CreateBinanceNetEnvironment();
            settings.ApiCredentials = new BinanceCredentials(ApiKey, ApiSecret);
            settings.RateLimiterEnabled = false;

            // On by default for sockets, off for REST: with it on, every order id would be rewritten with a broker prefix.
            settings.AllowAppendingClientOrderId = false;
            settings.ReconnectPolicy = ReconnectPolicy.Disabled;
        });

    /// <summary>Throws when our call failed, so a benchmark never measures a failure.</summary>
    public static void Check<TValue>(in RestApiRequestResult<TValue> result, string call)
    {
        if (result.Outcome != RequestOutcome.Success)
        {
            throw new InvalidOperationException(
                $"{call}: {result.Outcome}, {result.Errors.Reason}, status {result.Errors.StatusCode}. Is the stub running on {StubHost} with the recorded data?");
        }
    }

    /// <summary>Throws when our WebSocket API call failed, so a benchmark never measures a failure.</summary>
    public static void Check<TValue>(in WsApiRequestResult<TValue> result, string call)
    {
        if (result.Outcome != RequestOutcome.Success)
        {
            throw new InvalidOperationException(
                $"{call}: {result.Outcome}, {result.Errors.Reason}, status {result.Errors.StatusCode}, code {result.Errors.ExchangeCode}. Is the stub running on {StubHost} with the recorded data?");
        }
    }

    /// <summary>Throws when a Binance.Net call failed, so a benchmark never measures a failure.</summary>
    public static async Task CheckAsync<TValue>(Task<HttpResult<TValue>> call, string name)
    {
        HttpResult<TValue> result = await call.ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException($"{name}: {result.Error}. Is the stub running on {StubHost} with the recorded data?");
        }
    }

    /// <summary>Throws when a Binance.Net WebSocket API call failed, so a benchmark never measures a failure.</summary>
    public static async Task CheckAsync<TValue>(Task<QueryResult<TValue>> call, string name)
    {
        QueryResult<TValue> result = await call.ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException($"{name}: {result.Error}. Is the stub running on {StubHost} with the recorded data?");
        }
    }

    public void Dispose()
    {
        Axitor.Dispose();
        BinanceNet.Dispose();
    }
}
