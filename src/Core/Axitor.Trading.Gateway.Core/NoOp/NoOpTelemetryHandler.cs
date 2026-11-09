using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.NoOp;

/// <summary>Default handler: reports nothing.</summary>
public readonly struct NoOpTelemetryHandler : ITelemetryHandler
{
    /// <inheritdoc />
    public void OnRestApiRequestSucceeded(string requestUri) { }

    /// <inheritdoc />
    public void OnRestApiRequestFailed(string requestUri, in RestApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnRestApiRequestCanceled(string? requestUri, in RestApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnRestApiRequestDroppedByTtl(string? requestUri, in RestApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnRestApiRequestRetryAttempt(string? requestUri, in RestApiRequestResultError errors, in RestApiRequestResultResiliency resiliency) { }

    /// <inheritdoc />
    public void OnRestApiRequestRejected(in RestApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnWsApiConnected(in WsApiConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWsApiDisconnected(in WsApiConnectionInfo connection, in WsDisconnectInfo disconnect) { }

    /// <inheritdoc />
    public void OnWsApiConnectFailed(in WsApiConnectionInfo connection, in WsDisconnectInfo failure, int attempt) { }

    /// <inheritdoc />
    public void OnWsApiResponseUnmatched(in WsApiConnectionInfo connection, long requestId) { }

    /// <inheritdoc />
    public void OnWsApiMessageUnknown(in WsApiConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWsApiMessageNonStandard(in WsApiConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWsApiRequestSucceeded(Uri uri, string method, long requestId) { }

    /// <inheritdoc />
    public void OnWsApiRequestFailed(Uri uri, string method, in WsApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnWsApiRequestCanceled(Uri uri, string method, in WsApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnWsApiRequestDroppedByTtl(Uri uri, string method, in WsApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnWsApiRequestRetryAttempt(Uri uri, string method, in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency) { }

    /// <inheritdoc />
    public void OnWsApiRequestRejected(Uri uri, string method, in WsApiRequestResultError errors) { }

    /// <inheritdoc />
    public void OnWsStreamConnected(in WsStreamConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWsStreamDisconnected(in WsStreamConnectionInfo connection, in WsDisconnectInfo disconnect) { }

    /// <inheritdoc />
    public void OnWsStreamConnectFailed(in WsStreamConnectionInfo connection, in WsDisconnectInfo failure, int attempt) { }

    /// <inheritdoc />
    public void OnWsStreamReplyUnmatched(in WsStreamConnectionInfo connection, long requestId) { }

    /// <inheritdoc />
    public void OnWsStreamMessageUnknown(in WsStreamConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWsStreamMessageNonStandard(in WsStreamConnectionInfo connection) { }

    /// <inheritdoc />
    public void OnWatchdogSilent(long silenceMillis) { }

    /// <inheritdoc />
    public void OnWatchdogResumed(in WsStreamGap gap) { }

    /// <inheritdoc />
    public void OnWatchdogConnectFailed(in WsDisconnectInfo failure, int attempt) { }
}
