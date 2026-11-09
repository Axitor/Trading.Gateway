using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Wire;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Watchdog;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

namespace Axitor.Trading.Gateway.Core.Engine;

/// <summary>Exchange-agnostic engine client.</summary>
internal sealed class EngineClient<TTelemetry> : IDisposable where TTelemetry : struct, ITelemetryHandler
{
    private readonly BanGate _banGate;
    private readonly NetworkGate _networkGate;
    private readonly NetworkRepairWorker _networkRepairWorker;
    private readonly RestApiResiliencePipeline<TTelemetry> _restApiResiliencePipeline;
    private readonly WsApiTransportOptions _wsApiOptions;
    private readonly WsStreamTransportOptions _wsStreamOptions;
    private readonly TTelemetry _telemetry;

    // Created by the connector on init and with sessions that need their own connection; changes take the lock.
    private readonly Lock _wsApiSync = new();
    private readonly List<IDisposable> _wsApiPipelines = [];

    // Stream transports come with stream sessions, created at any time; changes take the lock.
    private readonly Lock _wsStreamSync = new();
    private readonly List<IDisposable> _wsStreamTransports = [];

    // Drops every stream connection when the watchdog barks. Replaced whole on change, read without a lock.
    private Action<WsDisconnectReason>[] _wsStreamAborts = [];

    /// <summary>Creates a client.</summary>
    public EngineClient(EngineOptions options, string? apiKey, string apiKeyHeader, Uri pingUri, TTelemetry telemetry, HttpMessageHandler? messageHandler)
    {
        _banGate = new();
        _networkGate = new();
        _networkRepairWorker = new(options, _networkGate, pingUri, options.RestApi.Network.PingInterval, messageHandler);
        _restApiResiliencePipeline = new RestApiResiliencePipeline<TTelemetry>(options, apiKey, apiKeyHeader, telemetry, messageHandler, _banGate, _networkGate, _networkRepairWorker);
        _wsApiOptions = options.WsApi;
        _wsStreamOptions = options.WsStream;
        _telemetry = telemetry;
    }

    /// <summary>Most subscriptions one stream transport can hold.</summary>
    public int WsStreamCapacity => _wsStreamOptions.Network.MaxConnections * _wsStreamOptions.Network.MaxStreamsPerConnection;

    /// <summary>Most streams on one stream connection.</summary>
    public int WsStreamsPerConnection => _wsStreamOptions.Network.MaxStreamsPerConnection;

    /// <summary>Reconnect back-off settings of the streams.</summary>
    public ResiliencyWsStreamOptions WsStreamResiliency => _wsStreamOptions.Resiliency;

    /// <summary>Reconnect back-off settings of the WebSocket API.</summary>
    public ResiliencyWsApiOptions WsApiResiliency => _wsApiOptions.Resiliency;

    /// <summary>REST API execution. A parser with no state is passed as default.</summary>
    public ValueTask<RestApiRequestResult<TValue>> SendRestApiAsync<TPayload, TAuth, TLimiter, TParser, TValue, TDialect>(
        RestApiEndpointDescriptor descriptor,
        TPayload payload,
        TAuth auth,
        TLimiter limiter,
        TParser parser,
        CancellationToken cancellationToken)
        where TPayload : struct, IPayloadWriter
        where TAuth : struct, IRequestAuthenticator
        where TParser : struct, IResponseParser<TValue>
        where TDialect : struct, IExchangeDialectRestApi
        where TLimiter : struct, IRateLimiter
    => _restApiResiliencePipeline.SendAsync<TPayload, TAuth, TLimiter, TParser, TValue, TDialect>(
        descriptor, payload, auth, limiter, parser, cancellationToken);

    /// <summary>Cold path. Fails a REST API request refused before sending and reports it. Nothing is sent.</summary>
    public RestApiRequestResult<TValue> RejectRestApi<TValue>(RestApiRequestErrorReason reason, int ruleViolation)
    {
        RestApiRequestResult<TValue> result = new()
        {
            Outcome = RequestOutcome.Failed,
            Errors = new RestApiRequestResultError() { Reason = reason, RuleViolation = ruleViolation },
            Resiliency = new RestApiRequestResultResiliency() { Action = RequestErrorAction.Skip },
        };

        _telemetry.OnRestApiRequestRejected(in result.Errors);
        return result;
    }

    /// <summary>
    /// Cold path. Creates a WebSocket API pipeline over the shared gates: on client init for each market, or when a session needs its own connection.
    /// A single lane serves Critical requests too; the hook prepares every new connection.
    /// </summary>
    public WsApiResiliencePipeline<TDialect, TLimiter, TEventDispatcher, TTelemetry> CreateWsApiPipeline<TDialect, TLimiter, TEventDispatcher>(
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter,
        TEventDispatcher dispatcher,
        IWsApiConnectionHook? hook = null,
        bool singleLane = false)
        where TDialect : struct, IExchangeDialectWsApi
        where TLimiter : struct, IRateLimiter
        where TEventDispatcher : struct, IWsApiEventDispatcher
    {
        WsApiResiliencePipeline<TDialect, TLimiter, TEventDispatcher, TTelemetry> pipeline = new(
            _wsApiOptions, uri, messagesPerSecond, connectLimitBucket, connectWeight,
            limiter, dispatcher, _telemetry, _banGate, _networkGate, _networkRepairWorker, hook, singleLane);

        lock (_wsApiSync)
        {
            _wsApiPipelines.Add(pipeline);
        }

        return pipeline;
    }

    /// <summary>
    /// Cold path. Creates the stream transport of one route over the shared gates. Call on init or when a stream session is created.
    /// Zero maxConnections takes the count from the options. A request header, when given, goes with every handshake, e.g. an API key.
    /// </summary>
    public WsStreamTransport<TDialect, TLimiter, TDispatcher, TTelemetry> CreateWsStreamTransport<TDialect, TLimiter, TDispatcher>(
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter,
        TDispatcher dispatcher,
        int maxConnections = 0,
        string? requestHeaderName = null,
        string? requestHeaderValue = null)
        where TDialect : struct, IExchangeDialectWsStream
        where TLimiter : struct, IRateLimiter
        where TDispatcher : struct, IWsStreamDispatcher
    {
        WsStreamTransportOptions options = maxConnections <= 0
            ? _wsStreamOptions
            : _wsStreamOptions with { Network = _wsStreamOptions.Network with { MaxConnections = maxConnections } };

        WsStreamTransport<TDialect, TLimiter, TDispatcher, TTelemetry> transport = new(
            options, uri, messagesPerSecond, connectLimitBucket, connectWeight,
            limiter, dispatcher, _telemetry, _banGate, _networkGate, _networkRepairWorker,
            isWatchdog: false, requestHeaderName, requestHeaderValue);

        lock (_wsStreamSync)
        {
            _wsStreamTransports.Add(transport);

            Action<WsDisconnectReason>[] aborts = [.. _wsStreamAborts, transport.AbortAll];
            Volatile.Write(ref _wsStreamAborts, aborts);
        }

        return transport;
    }

    /// <summary>Cold path. Creates the watchdog over the shared gates. Call on client init only.</summary>
    public WsStreamWatchdog<TDialect, TLimiter, TTelemetry> CreateWsStreamWatchdog<TDialect, TLimiter>(
        Uri uri,
        byte[] stream,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter)
        where TDialect : struct, IExchangeDialectWsStream
        where TLimiter : struct, IRateLimiter
    {
        WsStreamWatchdog<TDialect, TLimiter, TTelemetry> watchdog = new(
            _wsStreamOptions, uri, stream, messagesPerSecond, connectLimitBucket, connectWeight,
            limiter, _telemetry, _banGate, _networkGate, _networkRepairWorker, AbortAllStreams);

        lock (_wsStreamSync)
        {
            _wsStreamTransports.Add(watchdog);
        }

        return watchdog;
    }

    /// <summary>Drops every stream connection of the client. Each one reconnects once the network is repaired.</summary>
    private void AbortAllStreams(WsDisconnectReason reason)
    {
        foreach (Action<WsDisconnectReason> abort in Volatile.Read(ref _wsStreamAborts))
        {
            abort(reason);
        }
    }

    /// <summary>Dispose IDisposable resources.</summary>
    public void Dispose()
    {
        // WebSockets first: their connections use the repair worker.
        lock (_wsApiSync)
        {
            foreach (IDisposable pipeline in _wsApiPipelines)
            {
                pipeline.Dispose();
            }
        }

        lock (_wsStreamSync)
        {
            foreach (IDisposable transport in _wsStreamTransports)
            {
                transport.Dispose();
            }
        }

        _restApiResiliencePipeline.Dispose();
        _networkRepairWorker.Dispose();
    }
}
