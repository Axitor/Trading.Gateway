using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Handlers;

/// <summary>
/// Gets reports from the client: failures, retries, reconnects. Must be a readonly struct.
/// Calls run inside the library, some on receive loops: return fast and never throw.
/// In a real app, send reports to a queue or counters. Here they go to the console.
/// </summary>
internal readonly struct SampleTelemetryHandler : ITelemetryHandler
{
    // REST API

    // Called on every success. Keep it empty unless you count calls.
    public void OnRestApiRequestSucceeded(string requestUri) { }

    public void OnRestApiRequestFailed(string requestUri, in RestApiRequestResultError errors) =>
        Console.WriteLine($"REST failed: {errors.Reason}, HTTP {errors.StatusCode}, code {errors.ExchangeCode}");

    public void OnRestApiRequestCanceled(string? requestUri, in RestApiRequestResultError errors) { }

    public void OnRestApiRequestDroppedByTtl(string? requestUri, in RestApiRequestResultError errors) =>
        Console.WriteLine("REST request dropped: too late to send");

    public void OnRestApiRequestRetryAttempt(string? requestUri, in RestApiRequestResultError errors, in RestApiRequestResultResiliency resiliency) =>
        Console.WriteLine($"REST retry {resiliency.RetryNumber} in {resiliency.RetryDelayMillis} ms: {errors.Reason}");

    // An order that broke a symbol rule. Nothing was sent.
    public void OnRestApiRequestRejected(in RestApiRequestResultError errors) =>
        Console.WriteLine($"REST order rejected before sending: rule {errors.RuleViolation}");

    // WebSocket API

    public void OnWsApiConnected(in WsApiConnectionInfo connection) =>
        Console.WriteLine($"WS API connected: {connection.Uri}, lane {connection.Lane}");

    public void OnWsApiDisconnected(in WsApiConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        Console.WriteLine($"WS API disconnected: {connection.Uri}, {disconnect.Reason}");

    public void OnWsApiConnectFailed(in WsApiConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        Console.WriteLine($"WS API connect failed, attempt {attempt}: {failure.Reason}");

    // A response came too late, after its timeout.
    public void OnWsApiResponseUnmatched(in WsApiConnectionInfo connection, long requestId) =>
        Console.WriteLine($"WS API late response: {requestId}");

    public void OnWsApiMessageUnknown(in WsApiConnectionInfo connection) { }

    // An event with a value the library does not know, e.g. a new order status. The event was still delivered.
    public void OnWsApiMessageNonStandard(in WsApiConnectionInfo connection) =>
        Console.WriteLine("WS API event with an unknown value");

    public void OnWsApiRequestSucceeded(Uri uri, string method, long requestId) { }

    public void OnWsApiRequestFailed(Uri uri, string method, in WsApiRequestResultError errors) =>
        Console.WriteLine($"WS API {method} failed: {errors.Reason}, code {errors.ExchangeCode}");

    public void OnWsApiRequestCanceled(Uri uri, string method, in WsApiRequestResultError errors) { }

    public void OnWsApiRequestDroppedByTtl(Uri uri, string method, in WsApiRequestResultError errors) =>
        Console.WriteLine($"WS API {method} dropped: too late to send");

    public void OnWsApiRequestRetryAttempt(Uri uri, string method, in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency) =>
        Console.WriteLine($"WS API {method} retry {resiliency.RetryNumber}: {errors.Reason}");

    public void OnWsApiRequestRejected(Uri uri, string method, in WsApiRequestResultError errors) =>
        Console.WriteLine($"WS API {method} rejected before sending: rule {errors.RuleViolation}");

    // Streams

    public void OnWsStreamConnected(in WsStreamConnectionInfo connection) =>
        Console.WriteLine($"Stream connected: {connection.Uri}, shard {connection.Shard}");

    public void OnWsStreamDisconnected(in WsStreamConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        Console.WriteLine($"Stream disconnected: {connection.Uri}, {disconnect.Reason}");

    public void OnWsStreamConnectFailed(in WsStreamConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        Console.WriteLine($"Stream connect failed, attempt {attempt}: {failure.Reason}");

    public void OnWsStreamReplyUnmatched(in WsStreamConnectionInfo connection, long requestId) { }

    public void OnWsStreamMessageUnknown(in WsStreamConnectionInfo connection) { }

    public void OnWsStreamMessageNonStandard(in WsStreamConnectionInfo connection) =>
        Console.WriteLine("Stream message with an unknown value");

    // Watchdog: a stream that should never go quiet. Silence means the network may be down.

    public void OnWatchdogSilent(long silenceMillis) =>
        Console.WriteLine($"Watchdog: no data for {silenceMillis} ms, reconnecting streams");

    public void OnWatchdogResumed(in WsStreamGap gap) =>
        Console.WriteLine($"Watchdog: data is back after {gap.EndUtcMillis - gap.StartUtcMillis} ms");

    public void OnWatchdogConnectFailed(in WsDisconnectInfo failure, int attempt) =>
        Console.WriteLine($"Watchdog connect failed, attempt {attempt}: {failure.Reason}");
}
