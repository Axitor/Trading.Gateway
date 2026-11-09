using Axitor.Trading.Gateway.Binance.Authentication;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Watchdog;

namespace Axitor.Trading.Gateway.Binance.Session;

/// <summary>Entry point for the Binance connector. Create one per process and keep it.</summary>
/// <remarks>
/// <para>The API key and secret live on options; leave them unset for public endpoints only.</para>
/// <para>Options are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.</para>
/// <para>Specify the handler type on the client: BinanceClient&lt;NoOpTelemetryHandler&gt; to ignore reports, or BinanceClient&lt;YourHandler&gt; to receive reports.</para>
/// <para>
/// Rate limits are strictly configuration-driven. Deliberately avoid auto-fetching limits from exchange metadata endpoints.
/// The caller is responsible for supplying the correct limit parameters for their specific account tier.
/// </para>
/// <para>The WebSocket API of each market in BinanceOptions.WsApiMarkets connects on init; markets left out open no connection.</para>
/// <para>
/// Streams connect on demand. A stream session holds up to MaxConnections × MaxStreamsPerConnection subscriptions of EngineOptions.WsStream,
/// 5 × 200 by default; the exchange allows up to 1024 streams per connection.
/// Raise MaxConnections first: a connection handles its streams in turn, so more streams on it add latency on busy streams.
/// </para>
/// <para>The watchdog runs while any WebSocket API or stream market is enabled.</para>
/// </remarks>
public sealed class BinanceClient<TTelemetry> : IDisposable where TTelemetry : struct, ITelemetryHandler
{
    // WebSocket API handshake weights, as the exchange docs state them.
    private const int FuturesUsdMWsApiHandshakeWeight = 5;
    private const int FuturesCoinMWsApiHandshakeWeight = 5;
    internal const int SpotWsApiHandshakeWeight = 2;

    // Spot handshakes also count toward the spot stream connection window: the docs may mean one window for both.
    internal const BinanceRateLimitBucket SpotWsApiConnectLimitBucket = BinanceRateLimitBucket.SpotWsApiRequestWeight | BinanceRateLimitBucket.SpotStreamConnections;

    // Stream connection windows charge one per attempt; the weight is not used.
    private const int StreamConnectWeight = 1;

    private readonly EngineClient<TTelemetry> _engine;

    /// <summary>Futures endpoints.</summary>
    public Futures<TTelemetry> Futures { get; init; }

    /// <summary>Spot endpoints.</summary>
    public Spot<TTelemetry> Spot { get; init; }

    /// <summary>Most subscriptions one stream session can hold. Size LatestCache, ChannelSink and OrderBookSet by it.</summary>
    public int StreamSessionCapacity => _engine.WsStreamCapacity;

    /// <summary>App lifetime token the client was created with. Stops the background work of stock handlers.</summary>
    internal CancellationToken AppToken { get; }

    /// <summary>
    /// Creates a client with default options, passing only the API key and secret.
    /// Pass null for both to use public endpoints only.
    /// Specify the handler type on the client: BinanceClient&lt;NoOpTelemetryHandler&gt; to ignore reports,
    /// or BinanceClient&lt;YourHandler&gt; to receive reports.
    /// Pass the app lifetime token: WebSocket connections stop when it fires or on Dispose.
    /// </summary>
    public BinanceClient(string? key, string? secret, TTelemetry telemetry = default, CancellationToken appCancellationToken = default)
        : this(new BinanceOptions(key, secret), new EngineOptions(), telemetry, messageHandler: null, appCancellationToken)
    {
    }

    /// <summary>
    /// Creates a client with full control over both option sets.
    /// The key and secret live on BinanceOptions; leave them unset for public endpoints only.
    /// Specify the handler type on the client: BinanceClient&lt;NoOpTelemetryHandler&gt; to ignore reports,
    /// or BinanceClient&lt;YourHandler&gt; to receive reports.
    /// Pass the app lifetime token: WebSocket connections stop when it fires or on Dispose.
    /// Note: Options are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// </summary>
    public BinanceClient(BinanceOptions options, EngineOptions engineOptions, TTelemetry telemetry = default, CancellationToken appCancellationToken = default)
        : this(options, engineOptions, telemetry, messageHandler: null, appCancellationToken)
    {
    }

    // Internal construct
    internal BinanceClient(BinanceOptions options, EngineOptions engineOptions, TTelemetry telemetry, HttpMessageHandler? messageHandler, CancellationToken appCancellationToken = default)
    {
        AppToken = appCancellationToken;
        _engine = new EngineClient<TTelemetry>(engineOptions, options.Secret.ApiKey, options.Secret.ApiKeyHeader, options.Uri.PingURI, telemetry, messageHandler);
        BinanceRestApiAuthenticator auth = new(options.Secret.GetSecretBytes(), options.Limits.RecvWindowMilliseconds);
        BinanceWsApiAuthenticator wsApiAuth = new(options.Secret.GetApiKeyBytes(), options.Secret.GetSecretBytes(), options.Limits.RecvWindowMilliseconds);
        BinanceRateLimiterRef limiter = new(new BinanceRateLimiter(options.Limits));

        // Only enabled markets get a pipeline: each one holds its in-flight tables from the start.
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? futuresUsdMWsApi =
            (options.WsApiMarkets & BinanceMarkets.FuturesUsdM) != 0
                ? CreateWsApiPipeline(options.Uri.FuturesUsdWebSocketApi, BinanceRateLimitBucket.FuturesWsApiRequestWeight, FuturesUsdMWsApiHandshakeWeight, limiter)
                : null;

        // COIN-M WebSocket API weight is charged to the REST window, which USD-M shares.
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? futuresCoinMWsApi =
            (options.WsApiMarkets & BinanceMarkets.FuturesCoinM) != 0
                ? CreateWsApiPipeline(options.Uri.FuturesCoinWebSocketApi, BinanceRateLimitBucket.FuturesRequestWeight, FuturesCoinMWsApiHandshakeWeight, limiter)
                : null;

        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? spotWsApi =
            (options.WsApiMarkets & BinanceMarkets.Spot) != 0
                ? CreateWsApiPipeline(options.Uri.SpotWebSocketApi, SpotWsApiConnectLimitBucket, SpotWsApiHandshakeWeight, limiter)
                : null;

        BinanceRouter<TTelemetry> router = new(options, _engine, auth, limiter, wsApiAuth, futuresUsdMWsApi, futuresCoinMWsApi, spotWsApi, appCancellationToken);

        Futures = new Futures<TTelemetry>(router);
        Spot = new Spot<TTelemetry>(router);

        // Last: connections start opening in the background once the client is built.
        futuresUsdMWsApi?.Start(appCancellationToken);
        futuresCoinMWsApi?.Start(appCancellationToken);
        spotWsApi?.Start(appCancellationToken);

        // The watchdog guards every socket of the client, whatever market it trades.
        if ((options.WsApiMarkets | options.StreamMarkets) != BinanceMarkets.None)
        {
            WsStreamWatchdog<BinanceDialectWsStream, BinanceRateLimiterRef, TTelemetry> watchdog =
                _engine.CreateWsStreamWatchdog<BinanceDialectWsStream, BinanceRateLimiterRef>(
                    options.Uri.FuturesUsdWebSocketStreamPublic,
                    options.Uri.WatchdogStream,
                    options.Limits.FuturesWebsocketStreamLimitMessageAmountPerSecond,
                    (int)BinanceRateLimitBucket.FuturesStreamConnections,
                    StreamConnectWeight,
                    limiter);

            watchdog.Start(appCancellationToken);
        }
    }

    /// <summary>Cold path. WebSocket API pipeline of one market. No outgoing message limit: requests are limited by weight.</summary>
    private WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry> CreateWsApiPipeline(
        Uri uri,
        BinanceRateLimitBucket connectLimitBucket,
        int connectWeight,
        BinanceRateLimiterRef limiter) =>
        _engine.CreateWsApiPipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher>(
            uri,
            messagesPerSecond: 0,
            (int)connectLimitBucket,
            connectWeight,
            limiter,
            default);

    /// <summary>Dispose IDisposable resources.</summary>
    public void Dispose()
    {
        _engine.Dispose();
    }
}
