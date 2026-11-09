using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Authentication;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

namespace Axitor.Trading.Gateway.Binance.Session;

/// <summary>The exchange half of the send path.</summary>
internal sealed class BinanceRouter<TTelemetry>(
    BinanceOptions options,
    EngineClient<TTelemetry> engine,
    BinanceRestApiAuthenticator auth,
    BinanceRateLimiterRef limiter,
    BinanceWsApiAuthenticator wsApiAuth,
    WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? futuresUsdMWsApi,
    WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? futuresCoinMWsApi,
    WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? spotWsApi,
    CancellationToken appCancellationToken)
    where TTelemetry : struct, ITelemetryHandler
{
    // Stream connection windows charge one per attempt; the weight is not used.
    private const int StreamConnectWeight = 1;

    // User data keeps every event type on one connection, so their order is kept.
    private const int UserDataConnections = 1;

    // Channel of streams routed by their name.
    private const int NameChannel = 0;

    // Null when the market is left out of BinanceOptions.WsApiMarkets.
    private readonly WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? _futuresUsdMWsApi = futuresUsdMWsApi;
    private readonly WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? _futuresCoinMWsApi = futuresCoinMWsApi;
    private readonly WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>? _spotWsApi = spotWsApi;

    public BinanceOptions Options { get; init; } = options;
    private EngineClient<TTelemetry> Engine { get; init; } = engine;
    private BinanceRestApiAuthenticator Auth { get; init; } = auth;
    private BinanceRateLimiterRef Limiter { get; init; } = limiter;
    private BinanceWsApiAuthenticator WsApiAuth { get; init; } = wsApiAuth;
    private BinanceWsApiKeyAuthenticator WsApiKeyAuth { get; init; } = new(options.Secret.GetApiKeyBytes());
    private CancellationToken AppToken { get; init; } = appCancellationToken;

    /// <summary>USD-M futures WebSocket API. Throws InvalidOperationException when the market is not enabled.</summary>
    public WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry> FuturesUsdMWsApi =>
        _futuresUsdMWsApi ?? ThrowWsApiMarketNotEnabled<WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>>();

    /// <summary>COIN-M futures WebSocket API. Throws InvalidOperationException when the market is not enabled.</summary>
    public WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry> FuturesCoinMWsApi =>
        _futuresCoinMWsApi ?? ThrowWsApiMarketNotEnabled<WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>>();

    /// <summary>Spot WebSocket API. Throws InvalidOperationException when the market is not enabled.</summary>
    public WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry> SpotWsApi =>
        _spotWsApi ?? ThrowWsApiMarketNotEnabled<WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceWsApiEventDispatcher, TTelemetry>>();

    /// <summary>Sends a public REST API endpoint. The authenticator is the no-op one, so signing vanishes. A parser with no state can be left out.</summary>
    public ValueTask<RestApiRequestResult<TValue>> SendRestApiPublicAsync<TPayload, TParser, TValue>(
        RestApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken,
        TParser parser = default)
        where TPayload : struct, IPayloadWriter
        where TParser : struct, IResponseParser<TValue>
     => Engine.SendRestApiAsync<TPayload, NoOpAuthenticator, BinanceRateLimiterRef, TParser, TValue, BinanceDialectRestApi>(
            descriptor, payload, default, Limiter, parser, cancellationToken);

    /// <summary>Sends a signed REST API endpoint. A parser with no state can be left out.</summary>
    public ValueTask<RestApiRequestResult<TValue>> SendRestApiSignedAsync<TPayload, TParser, TValue>(
        RestApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken,
        TParser parser = default)
        where TPayload : struct, IPayloadWriter
        where TParser : struct, IResponseParser<TValue>
     => Engine.SendRestApiAsync<TPayload, BinanceRestApiAuthenticator, BinanceRateLimiterRef, TParser, TValue, BinanceDialectRestApi>(
            descriptor, payload, Auth, Limiter, parser, cancellationToken);

    /// <summary>Cold path. Fails an order that broke a symbol rule without sending it.</summary>
    public ValueTask<RestApiRequestResult<TValue>> RejectRestApi<TValue>(OrderRuleViolation violation) =>
        new(Engine.RejectRestApi<TValue>(RestApiRequestErrorReason.OrderFiltersViolated, (int)violation));

    /// <summary>Sends a public WebSocket API endpoint on the pipeline of its market.</summary>
    [SuppressMessage("Performance", "CA1822", Justification = "Kept an instance member so endpoints call public and signed sends the same way.")]
    public ValueTask<WsApiRequestResult<TValue>> SendWsApiPublicAsync<TEventDispatcher, TPayload, TParser, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken)
        where TEventDispatcher : struct, IWsApiEventDispatcher
        where TPayload : struct, IWsApiRequestWriter
        where TParser : struct, IWsApiResultParser<TValue>
     => pipeline.SendAsync<TPayload, NoOpWsApiAuthenticator, TParser, TValue>(descriptor, payload, default, cancellationToken);

    /// <summary>Sends a WebSocket API endpoint that takes only the apiKey, on the pipeline of its market.</summary>
    public ValueTask<WsApiRequestResult<TValue>> SendWsApiKeyAsync<TEventDispatcher, TPayload, TParser, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken)
        where TEventDispatcher : struct, IWsApiEventDispatcher
        where TPayload : struct, IWsApiRequestWriter
        where TParser : struct, IWsApiResultParser<TValue>
     => pipeline.SendAsync<TPayload, BinanceWsApiKeyAuthenticator, TParser, TValue>(descriptor, payload, WsApiKeyAuth, cancellationToken);

    /// <summary>Sends a signed WebSocket API endpoint on the pipeline of its market.</summary>
    public ValueTask<WsApiRequestResult<TValue>> SendWsApiSignedAsync<TEventDispatcher, TPayload, TParser, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken)
        where TEventDispatcher : struct, IWsApiEventDispatcher
        where TPayload : struct, IWsApiRequestWriter
        where TParser : struct, IWsApiResultParser<TValue>
     => pipeline.SendAsync<TPayload, BinanceWsApiAuthenticator, TParser, TValue>(descriptor, payload, WsApiAuth, cancellationToken);

    /// <summary>Sends a public WebSocket API request strictly on the ticket's connection, with no retries.</summary>
    [SuppressMessage("Performance", "CA1822", Justification = "Kept an instance member so endpoints call public and signed sends the same way.")]
    public ValueTask<WsApiRequestResult<TValue>> SendWsApiPublicOnAsync<TEventDispatcher, TPayload, TParser, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiConnectionTicket ticket,
        WsApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken)
        where TEventDispatcher : struct, IWsApiEventDispatcher
        where TPayload : struct, IWsApiRequestWriter
        where TParser : struct, IWsApiResultParser<TValue>
     => pipeline.SendOnAsync<TPayload, NoOpWsApiAuthenticator, TParser, TValue>(ticket, descriptor, payload, default, cancellationToken);

    /// <summary>Sends a signed WebSocket API request strictly on the ticket's connection, with no retries.</summary>
    public ValueTask<WsApiRequestResult<TValue>> SendWsApiSignedOnAsync<TEventDispatcher, TPayload, TParser, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        WsApiConnectionTicket ticket,
        WsApiEndpointDescriptor descriptor,
        TPayload payload,
        CancellationToken cancellationToken)
        where TEventDispatcher : struct, IWsApiEventDispatcher
        where TPayload : struct, IWsApiRequestWriter
        where TParser : struct, IWsApiResultParser<TValue>
     => pipeline.SendOnAsync<TPayload, BinanceWsApiAuthenticator, TParser, TValue>(ticket, descriptor, payload, WsApiAuth, cancellationToken);

    /// <summary>Cold path. Fails a WebSocket API order that broke a symbol rule without sending it.</summary>
    [SuppressMessage("Performance", "CA1822", Justification = "Kept an instance member so endpoints reject REST and WebSocket API orders the same way.")]
    public ValueTask<WsApiRequestResult<TValue>> RejectWsApi<TEventDispatcher, TValue>(
        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, TEventDispatcher, TTelemetry> pipeline,
        string method,
        OrderRuleViolation violation)
        where TEventDispatcher : struct, IWsApiEventDispatcher
     => new(pipeline.Reject<TValue>(method, WsApiRequestErrorReason.OrderFiltersViolated, (int)violation));

    /// <summary>Cold path. Creates a stream session on its own transport. Throws when the streams of the market are not enabled.</summary>
    public BinanceStreamSession CreateStreamSession<TParser, TModel, THandler>(BinanceMarkets market, Uri uri, byte[] nameSuffix, THandler handler)
        where TParser : struct, IBinanceStreamParser<TModel>
        where TModel : struct
        where THandler : struct, IBinanceStreamHandler<TModel>
    {
        BinanceStreamDispatcher<TParser, TModel, THandler> dispatcher = new(handler, Engine.WsStreamCapacity);
        return CreateSession<BinanceDialectWsStream, BinanceStreamDispatcher<TParser, TModel, THandler>>(market, uri, nameSuffix, NameChannel, dispatcher);
    }

    /// <summary>
    /// Cold path. Creates a depth stream session on its own transport. Throws when the streams of the market are not enabled,
    /// and when an order book set of another market, or one already serving a session, is passed.
    /// </summary>
    public BinanceStreamSession CreateDepthSession<TParser, THandler>(BinanceMarkets market, Uri uri, byte[] nameSuffix, THandler handler)
        where TParser : struct, IBinanceDepthParser
        where THandler : struct, IBinanceDepthHandler
    {
        // Checked before the set is bound, so a disabled market leaves the set free.
        if ((Options.StreamMarkets & market) == 0)
        {
            ThrowStreamsNotEnabled();
        }

        // Before the transport starts: a refused set leaves nothing running.
        if (handler is OrderBookSetHandler<TTelemetry> setHandler)
        {
            setHandler.Bind(market);
        }

        BinanceDepthDispatcher<TParser, THandler> dispatcher = new(handler, Engine.WsStreamCapacity);
        return CreateSession<BinanceDialectWsStream, BinanceDepthDispatcher<TParser, THandler>>(market, uri, nameSuffix, NameChannel, dispatcher);
    }

    /// <summary>
    /// Cold path. Creates a futures user data session on its own single connection. Throws when the streams or the WebSocket API of the market are not enabled.
    /// The session refuses events outside supportedEvents.
    /// </summary>
    public BinanceUserDataSession CreateUserDataSession<THandler>(BinanceMarkets market, Uri uri, BinanceUserDataEvents supportedEvents, THandler handler)
        where THandler : struct, IBinanceUserDataHandler
    {
        if ((Options.StreamMarkets & market) == 0)
        {
            ThrowStreamsNotEnabled();
        }

        // The listen key is kept through the WebSocket API; there is no REST fallback.
        if ((Options.WsApiMarkets & market) == 0)
        {
            ThrowWsApiNotEnabled();
        }

        BinanceUserDataState state = new(UserDataConnections * Engine.WsStreamsPerConnection);
        BinanceUserDataDispatcher<THandler> dispatcher = new(handler, state);

        // USD-M and COIN-M share the futures connection window.
        WsStreamTransport<BinanceDialectWsStream, BinanceRateLimiterRef, BinanceUserDataDispatcher<THandler>, TTelemetry> transport =
            Engine.CreateWsStreamTransport<BinanceDialectWsStream, BinanceRateLimiterRef, BinanceUserDataDispatcher<THandler>>(
                uri,
                Options.Limits.FuturesWebsocketStreamLimitMessageAmountPerSecond,
                (int)BinanceRateLimitBucket.FuturesStreamConnections,
                StreamConnectWeight,
                Limiter,
                dispatcher,
                UserDataConnections);

        transport.Start(AppToken);

        return new BinanceUserDataSessionCore<THandler, TTelemetry>(
            transport, state, new ListenKeyEndpoint<TTelemetry>(this, market), handler, supportedEvents,
            Options.UserDataKeepAliveInterval, Engine.WsStreamResiliency, AppToken);
    }

    /// <summary>
    /// Cold path. Creates a spot user data session on its own single-lane WebSocket API connection, which opens at once.
    /// Throws when the spot WebSocket API is not enabled.
    /// </summary>
    public BinanceSpotUserDataSession CreateSpotUserDataSession<THandler>(THandler handler)
        where THandler : struct, IBinanceSpotUserDataHandler
    {
        // Its own connection rather than the shared spot pipeline, under the same market switch.
        if ((Options.WsApiMarkets & BinanceMarkets.Spot) == 0)
        {
            ThrowWsApiNotEnabled();
        }

        BinanceSpotUserDataState state = new();

        BinanceSpotUserDataSessionCore<THandler, TTelemetry> session = new(
            state, new SubscriptionEndpoint<TTelemetry>(this), handler, Engine.WsApiResiliency, AppToken);

        WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceSpotUserDataDispatcher<THandler>, TTelemetry> pipeline =
            Engine.CreateWsApiPipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceSpotUserDataDispatcher<THandler>>(
                Options.Uri.SpotWebSocketApi,
                messagesPerSecond: 0,
                (int)BinanceClient<TTelemetry>.SpotWsApiConnectLimitBucket,
                BinanceClient<TTelemetry>.SpotWsApiHandshakeWeight,
                Limiter,
                new BinanceSpotUserDataDispatcher<THandler>(handler, state),
                hook: session,
                singleLane: true);

        // The hook may run as soon as the pipeline starts, so the session is bound first.
        session.Bind(pipeline);
        pipeline.Start(AppToken);

        return session;
    }

    /// <summary>
    /// Cold path. Creates a spot SBE stream session on its own transport.
    /// Throws when spot streams are not enabled or the Ed25519 API key is not set.
    /// </summary>
    public BinanceStreamSession CreateSbeStreamSession<TParser, TModel, THandler>(int templateId, byte[] nameSuffix, THandler handler)
        where TParser : struct, IBinanceStreamParser<TModel>
        where TModel : struct
        where THandler : struct, IBinanceStreamHandler<TModel>
    {
        string apiKey = GetSbeApiKey();
        BinanceStreamDispatcher<TParser, TModel, THandler> dispatcher = new(handler, Engine.WsStreamCapacity);
        return CreateSbeSession(templateId, nameSuffix, dispatcher, apiKey);
    }

    /// <summary>
    /// Cold path. Creates a spot SBE trades session on its own transport: each trade of an event goes to the handler on its own.
    /// Throws when spot streams are not enabled or the Ed25519 API key is not set.
    /// </summary>
    public BinanceStreamSession CreateSbeTradeSession<THandler>(byte[] nameSuffix, THandler handler)
        where THandler : struct, IBinanceStreamHandler<SbeTradeModel>
    {
        string apiKey = GetSbeApiKey();
        BinanceSbeTradeDispatcher<THandler> dispatcher = new(handler, Engine.WsStreamCapacity);
        return CreateSbeSession(BinanceSbeReader.TradesTemplateId, nameSuffix, dispatcher, apiKey);
    }

    /// <summary>
    /// Cold path. Creates a spot SBE depth session on its own transport. Throws when spot streams are not enabled or the Ed25519 API key is not set,
    /// and when an order book set of another market, or one already serving a session, is passed.
    /// </summary>
    public BinanceStreamSession CreateSbeDepthSession<THandler>(byte[] nameSuffix, THandler handler)
        where THandler : struct, IBinanceDepthHandler
    {
        // Checked before the set is bound, so a refused session leaves the set free.
        string apiKey = GetSbeApiKey();

        // Before the transport starts: a refused set leaves nothing running.
        if (handler is OrderBookSetHandler<TTelemetry> setHandler)
        {
            setHandler.Bind(BinanceMarkets.Spot);
        }

        BinanceDepthDispatcher<SbeDepthUpdateParser, THandler> dispatcher = new(handler, Engine.WsStreamCapacity);
        return CreateSbeSession(BinanceSbeReader.DepthDiffTemplateId, nameSuffix, dispatcher, apiKey);
    }

    /// <summary>
    /// Cold path. Creates a session of any dialect and dispatcher on its own transport. Throws when the streams of the market are not enabled.
    /// The channel tells the session how the dialect routes data; a request header, when given, goes with every handshake.
    /// </summary>
    private BinanceStreamSessionCore<TDialect, TDispatcher, TTelemetry> CreateSession<TDialect, TDispatcher>(
        BinanceMarkets market,
        Uri uri,
        byte[] nameSuffix,
        int channel,
        TDispatcher dispatcher,
        string? requestHeaderName = null,
        string? requestHeaderValue = null)
        where TDialect : struct, IExchangeDialectWsStream
        where TDispatcher : struct, IWsStreamDispatcher
    {
        if ((Options.StreamMarkets & market) == 0)
        {
            ThrowStreamsNotEnabled();
        }

        // USD-M and COIN-M share the futures connection window.
        bool isSpot = market == BinanceMarkets.Spot;
        int messagesPerSecond = isSpot ? Options.Limits.SpotWebsocketStreamLimitMessageAmountPerSecond : Options.Limits.FuturesWebsocketStreamLimitMessageAmountPerSecond;
        int connectLimitBucket = (int)(isSpot ? BinanceRateLimitBucket.SpotStreamConnections : BinanceRateLimitBucket.FuturesStreamConnections);

        WsStreamTransport<TDialect, BinanceRateLimiterRef, TDispatcher, TTelemetry> transport =
            Engine.CreateWsStreamTransport<TDialect, BinanceRateLimiterRef, TDispatcher>(
                uri, messagesPerSecond, connectLimitBucket, StreamConnectWeight, Limiter, dispatcher,
                maxConnections: 0, requestHeaderName, requestHeaderValue);

        transport.Start(AppToken);
        return new BinanceStreamSessionCore<TDialect, TDispatcher, TTelemetry>(transport, nameSuffix, channel);
    }

    /// <summary>Cold path. A spot SBE session routed by the template id; the Ed25519 API key goes with every handshake.</summary>
    private BinanceStreamSessionCore<BinanceDialectSbeStream, TDispatcher, TTelemetry> CreateSbeSession<TDispatcher>(int templateId, byte[] nameSuffix, TDispatcher dispatcher, string apiKey)
        where TDispatcher : struct, IWsStreamDispatcher =>
        // Counted in the spot stream connection window: the docs give the SBE host the same limit, read pessimistically as one window.
        CreateSession<BinanceDialectSbeStream, TDispatcher>(
            BinanceMarkets.Spot, Options.Uri.SpotSbeStream, nameSuffix, templateId, dispatcher, Options.Secret.ApiKeyHeader, apiKey);

    /// <summary>Cold path. The Ed25519 API key the SBE handshake carries. Throws when spot streams are not enabled or the key is not set.</summary>
    private string GetSbeApiKey()
    {
        if ((Options.StreamMarkets & BinanceMarkets.Spot) == 0)
        {
            ThrowStreamsNotEnabled();
        }

        string? apiKey = Options.Secret.Ed25519ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            ThrowSbeKeyMissing();
        }

        return apiKey;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowStreamsNotEnabled() =>
        throw new InvalidOperationException("The streams of this market are not enabled in BinanceOptions.StreamMarkets.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowWsApiNotEnabled() =>
        throw new InvalidOperationException("User data needs the WebSocket API of this market, which is not enabled in BinanceOptions.WsApiMarkets.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowWsApiMarketNotEnabled<T>() =>
        throw new InvalidOperationException("The WebSocket API of this market is not enabled in BinanceOptions.WsApiMarkets.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowSbeKeyMissing() =>
        throw new InvalidOperationException("The SBE streams need an Ed25519 API key in BinanceSecret.Ed25519ApiKey.");
}
