using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>
/// Receives engine events; the library itself logs nothing.
/// Implementations must return fast and never throw: they run inside receive loops and reconnect logic.
/// </summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Passed as a generic struct, not as events: every report is a direct call the compiler can inline,
/// and an empty handler costs nothing (ADR-0005).
/// </para>
/// <para>
/// DESIGN NOTE: Implement as a readonly struct. Handlers sit in readonly fields, and a mutable struct there
/// gets a hidden defensive copy on every call.
/// </para>
/// </remarks>
public interface ITelemetryHandler
{
    /// <summary>A REST API request succeeded.</summary>
    /// <param name="requestUri">The full request URI, provided for request identification.</param>
    public void OnRestApiRequestSucceeded(string requestUri);

    /// <summary>A REST API request failed.</summary>
    /// <param name="requestUri"><inheritdoc cref="OnRestApiRequestSucceeded" path="/param[@name='requestUri']/node()"/></param>
    /// <param name="errors">Detailed information about the failure.</param>
    public void OnRestApiRequestFailed(string requestUri, in RestApiRequestResultError errors);

    /// <summary>
    /// A REST API request was canceled.
    /// Note: The full request URI can be absent at the stage.
    /// </summary>
    /// <param name="requestUri"><inheritdoc cref="OnRestApiRequestSucceeded" path="/param[@name='requestUri']/node()"/></param>
    /// <param name="errors"><inheritdoc cref="OnRestApiRequestFailed" path="/param[@name='errors']/node()"/></param>
    public void OnRestApiRequestCanceled(string? requestUri, in RestApiRequestResultError errors);

    /// <summary>
    /// A REST API request was dropped by Time-To-Live.
    /// Note: The full request URI can be absent at the stage.
    /// </summary>
    /// <param name="requestUri"><inheritdoc cref="OnRestApiRequestSucceeded" path="/param[@name='requestUri']/node()"/></param>
    /// <param name="errors"><inheritdoc cref="OnRestApiRequestFailed" path="/param[@name='errors']/node()"/></param>
    public void OnRestApiRequestDroppedByTtl(string? requestUri, in RestApiRequestResultError errors);

    /// <summary>
    /// A REST API request retry attempt.
    /// Note: The full request URI can be absent at the stage.
    /// </summary>
    /// <param name="requestUri"><inheritdoc cref="OnRestApiRequestSucceeded" path="/param[@name='requestUri']/node()"/></param>
    /// <param name="errors"><inheritdoc cref="OnRestApiRequestFailed" path="/param[@name='errors']/node()"/></param>
    /// <param name="resiliency">Detailed information about retries.</param>
    public void OnRestApiRequestRetryAttempt(string? requestUri, in RestApiRequestResultError errors, in RestApiRequestResultResiliency resiliency);

    /// <summary>A REST API request was refused before sending, e.g. an order that broke a symbol rule. Nothing reached the exchange.</summary>
    /// <param name="errors"><inheritdoc cref="OnRestApiRequestFailed" path="/param[@name='errors']/node()"/></param>
    public void OnRestApiRequestRejected(in RestApiRequestResultError errors);

    /// <summary>A WebSocket API connection opened.</summary>
    public void OnWsApiConnected(in WsApiConnectionInfo connection);

    /// <summary>A WebSocket API connection ended, for any reason including planned rotation.</summary>
    public void OnWsApiDisconnected(in WsApiConnectionInfo connection, in WsDisconnectInfo disconnect);

    /// <summary>A WebSocket API connect attempt failed. Attempt counts from one.</summary>
    public void OnWsApiConnectFailed(in WsApiConnectionInfo connection, in WsDisconnectInfo failure, int attempt);

    /// <summary>A response came for no waiting request, e.g. after its timeout.</summary>
    public void OnWsApiResponseUnmatched(in WsApiConnectionInfo connection, long requestId);

    /// <summary>A message of unknown shape was dropped.</summary>
    public void OnWsApiMessageUnknown(in WsApiConnectionInfo connection);

    /// <summary>An event was delivered with a value the connector did not recognize or could not fit, e.g. a new order status.</summary>
    public void OnWsApiMessageNonStandard(in WsApiConnectionInfo connection);

    /// <summary>A WebSocket API request succeeded. The uri tells the market, the method the call.</summary>
    public void OnWsApiRequestSucceeded(Uri uri, string method, long requestId);

    /// <summary>A WebSocket API request attempt failed. The request id is in errors.</summary>
    public void OnWsApiRequestFailed(Uri uri, string method, in WsApiRequestResultError errors);

    /// <summary>A WebSocket API request was canceled.</summary>
    public void OnWsApiRequestCanceled(Uri uri, string method, in WsApiRequestResultError errors);

    /// <summary>A WebSocket API request was dropped by Time-To-Live.</summary>
    public void OnWsApiRequestDroppedByTtl(Uri uri, string method, in WsApiRequestResultError errors);

    /// <summary>A WebSocket API request retry attempt.</summary>
    public void OnWsApiRequestRetryAttempt(Uri uri, string method, in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency);

    /// <summary>A WebSocket API request was refused before sending, e.g. an order that broke a symbol rule. Nothing reached the exchange.</summary>
    public void OnWsApiRequestRejected(Uri uri, string method, in WsApiRequestResultError errors);

    /// <summary>A stream connection opened.</summary>
    public void OnWsStreamConnected(in WsStreamConnectionInfo connection);

    /// <summary>A stream connection ended, for any reason including planned rotation.</summary>
    public void OnWsStreamDisconnected(in WsStreamConnectionInfo connection, in WsDisconnectInfo disconnect);

    /// <summary>A stream connect attempt failed. Attempt counts from one.</summary>
    public void OnWsStreamConnectFailed(in WsStreamConnectionInfo connection, in WsDisconnectInfo failure, int attempt);

    /// <summary>A reply came for no waiting subscribe or unsubscribe request, e.g. after its timeout.</summary>
    public void OnWsStreamReplyUnmatched(in WsStreamConnectionInfo connection, long requestId);

    /// <summary>A message of unknown shape, or for a stream no longer subscribed, was dropped.</summary>
    public void OnWsStreamMessageUnknown(in WsStreamConnectionInfo connection);

    /// <summary>A message was delivered with a value the connector did not recognize or could not fit, e.g. a new order status.</summary>
    public void OnWsStreamMessageNonStandard(in WsStreamConnectionInfo connection);

    /// <summary>The watchdog stream went quiet for this long. The network is checked and every stream connection reconnects.</summary>
    public void OnWatchdogSilent(long silenceMillis);

    /// <summary>The watchdog stream delivers again after a gap: a silence or a dropped connection.</summary>
    public void OnWatchdogResumed(in WsStreamGap gap);

    /// <summary>A watchdog connect attempt failed. Attempt counts from one.</summary>
    public void OnWatchdogConnectFailed(in WsDisconnectInfo failure, int attempt);
}
