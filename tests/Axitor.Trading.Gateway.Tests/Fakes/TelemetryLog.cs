using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Fakes;

/// <summary>Which telemetry method was called.</summary>
internal enum TelemetryEventKind : byte
{
    RestApiRequestSucceeded,
    RestApiRequestFailed,
    RestApiRequestCanceled,
    RestApiRequestDroppedByTtl,
    RestApiRequestRetryAttempt,
    RestApiRequestRejected,
    WsApiConnected,
    WsApiDisconnected,
    WsApiConnectFailed,
    WsApiResponseUnmatched,
    WsApiMessageUnknown,
    WsApiMessageNonStandard,
    WsApiRequestSucceeded,
    WsApiRequestFailed,
    WsApiRequestCanceled,
    WsApiRequestDroppedByTtl,
    WsApiRequestRetryAttempt,
    WsApiRequestRejected,
    WsStreamConnected,
    WsStreamDisconnected,
    WsStreamConnectFailed,
    WsStreamReplyUnmatched,
    WsStreamMessageUnknown,
    WsStreamMessageNonStandard,
    WatchdogSilent,
    WatchdogResumed,
    WatchdogConnectFailed,
}

/// <summary>One telemetry call with its arguments. Fields the call does not have stay default.</summary>
internal readonly record struct TelemetryEvent
{
    public TelemetryEventKind Kind { get; init; }

    /// <summary>REST request URI. Can be null on cancel, TTL drop and retry.</summary>
    public string? RequestUri { get; init; }

    /// <summary>WebSocket API endpoint of a request event.</summary>
    public Uri? Uri { get; init; }

    /// <summary>WebSocket API method of a request event.</summary>
    public string? Method { get; init; }

    public WsApiConnectionInfo WsApiConnection { get; init; }

    public WsStreamConnectionInfo WsStreamConnection { get; init; }

    public RestApiRequestResultError RestErrors { get; init; }

    public RestApiRequestResultResiliency RestResiliency { get; init; }

    public WsApiRequestResultError WsApiErrors { get; init; }

    public WsApiRequestResultResiliency WsApiResiliency { get; init; }

    /// <summary>Why a connection ended or a connect attempt failed.</summary>
    public WsDisconnectInfo Disconnect { get; init; }

    public WsStreamGap Gap { get; init; }

    /// <summary>Connect attempt, request id or watchdog silence in milliseconds, by kind.</summary>
    public long Number { get; init; }
}

/// <summary>Records every telemetry call in order and lets a test wait for one. Safe to read while background loops write.</summary>
internal sealed class TelemetryLog
{
    private readonly Lock _sync = new();
    private readonly List<TelemetryEvent> _events = [];

    public TelemetryLogHandler Handler => new(this);

    public IReadOnlyList<TelemetryEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Events of one kind, in order.</summary>
    public IReadOnlyList<TelemetryEvent> Of(TelemetryEventKind kind)
    {
        lock (_sync)
        {
            List<TelemetryEvent> found = [];

            foreach (TelemetryEvent value in _events)
            {
                if (value.Kind == kind)
                {
                    found.Add(value);
                }
            }

            return found;
        }
    }

    public int Count(TelemetryEventKind kind)
    {
        lock (_sync)
        {
            int count = 0;

            foreach (TelemetryEvent value in _events)
            {
                if (value.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Waits until at least count events of the kind have come.</summary>
    public Task WaitForAsync(TelemetryEventKind kind, int count = 1) => Wait.UntilAsync(() => count <= Count(kind));

    public void Add(in TelemetryEvent value)
    {
        lock (_sync)
        {
            _events.Add(value);
        }
    }
}

/// <summary>Telemetry handler that writes every call into its log.</summary>
internal readonly struct TelemetryLogHandler : ITelemetryHandler
{
    private readonly TelemetryLog _log;

    public TelemetryLogHandler(TelemetryLog log) => _log = log;

    public void OnRestApiRequestSucceeded(string requestUri) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestSucceeded, RequestUri = requestUri });

    public void OnRestApiRequestFailed(string requestUri, in RestApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestFailed, RequestUri = requestUri, RestErrors = errors });

    public void OnRestApiRequestCanceled(string? requestUri, in RestApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestCanceled, RequestUri = requestUri, RestErrors = errors });

    public void OnRestApiRequestDroppedByTtl(string? requestUri, in RestApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestDroppedByTtl, RequestUri = requestUri, RestErrors = errors });

    public void OnRestApiRequestRetryAttempt(string? requestUri, in RestApiRequestResultError errors, in RestApiRequestResultResiliency resiliency) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestRetryAttempt, RequestUri = requestUri, RestErrors = errors, RestResiliency = resiliency });

    public void OnRestApiRequestRejected(in RestApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.RestApiRequestRejected, RestErrors = errors });

    public void OnWsApiConnected(in WsApiConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiConnected, WsApiConnection = connection });

    public void OnWsApiDisconnected(in WsApiConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiDisconnected, WsApiConnection = connection, Disconnect = disconnect });

    public void OnWsApiConnectFailed(in WsApiConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiConnectFailed, WsApiConnection = connection, Disconnect = failure, Number = attempt });

    public void OnWsApiResponseUnmatched(in WsApiConnectionInfo connection, long requestId) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiResponseUnmatched, WsApiConnection = connection, Number = requestId });

    public void OnWsApiMessageUnknown(in WsApiConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiMessageUnknown, WsApiConnection = connection });

    public void OnWsApiMessageNonStandard(in WsApiConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiMessageNonStandard, WsApiConnection = connection });

    public void OnWsApiRequestSucceeded(Uri uri, string method, long requestId) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestSucceeded, Uri = uri, Method = method, Number = requestId });

    public void OnWsApiRequestFailed(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestFailed, Uri = uri, Method = method, WsApiErrors = errors });

    public void OnWsApiRequestCanceled(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestCanceled, Uri = uri, Method = method, WsApiErrors = errors });

    public void OnWsApiRequestDroppedByTtl(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestDroppedByTtl, Uri = uri, Method = method, WsApiErrors = errors });

    public void OnWsApiRequestRetryAttempt(Uri uri, string method, in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestRetryAttempt, Uri = uri, Method = method, WsApiErrors = errors, WsApiResiliency = resiliency });

    public void OnWsApiRequestRejected(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsApiRequestRejected, Uri = uri, Method = method, WsApiErrors = errors });

    public void OnWsStreamConnected(in WsStreamConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamConnected, WsStreamConnection = connection });

    public void OnWsStreamDisconnected(in WsStreamConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamDisconnected, WsStreamConnection = connection, Disconnect = disconnect });

    public void OnWsStreamConnectFailed(in WsStreamConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamConnectFailed, WsStreamConnection = connection, Disconnect = failure, Number = attempt });

    public void OnWsStreamReplyUnmatched(in WsStreamConnectionInfo connection, long requestId) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamReplyUnmatched, WsStreamConnection = connection, Number = requestId });

    public void OnWsStreamMessageUnknown(in WsStreamConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamMessageUnknown, WsStreamConnection = connection });

    public void OnWsStreamMessageNonStandard(in WsStreamConnectionInfo connection) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WsStreamMessageNonStandard, WsStreamConnection = connection });

    public void OnWatchdogSilent(long silenceMillis) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WatchdogSilent, Number = silenceMillis });

    public void OnWatchdogResumed(in WsStreamGap gap) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WatchdogResumed, Gap = gap });

    public void OnWatchdogConnectFailed(in WsDisconnectInfo failure, int attempt) =>
        _log.Add(new TelemetryEvent { Kind = TelemetryEventKind.WatchdogConnectFailed, Disconnect = failure, Number = attempt });
}
