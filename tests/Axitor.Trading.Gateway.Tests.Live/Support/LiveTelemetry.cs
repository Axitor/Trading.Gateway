using System.Collections.Concurrent;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Live.Support;

/// <summary>Reports of one live client. Every report goes to the test output; contract breaks are also kept for the assert.</summary>
internal sealed class LiveTelemetryLog
{
    private readonly ITestOutputHelper? _output = TestContext.Current.TestOutputHelper;
    private readonly ConcurrentQueue<string> _contractBreaks = new();

    /// <summary>Fails the test if any message had an unknown shape or a value the connector did not recognize.</summary>
    public void AssertContractKept() =>
        Assert.True(_contractBreaks.IsEmpty, "Contract breaks:" + Environment.NewLine + string.Join(Environment.NewLine, _contractBreaks));

    public void Report(string line) => Write(line);

    public void ReportContractBreak(string line)
    {
        _contractBreaks.Enqueue(line);
        Write(line);
    }

    private void Write(string line)
    {
        try
        {
            _output?.WriteLine(line);
        }
        catch (InvalidOperationException)
        {
            // Background loops can still report after the test has ended, when its output is already closed.
        }
    }
}

/// <summary>Telemetry handler of live clients. Writes every report into its log.</summary>
internal readonly struct LiveTelemetry : ITelemetryHandler
{
    private readonly LiveTelemetryLog _log;

    public LiveTelemetry(LiveTelemetryLog log) => _log = log;

    // REST API

    public void OnRestApiRequestSucceeded(string requestUri) =>
        _log.Report($"REST ok: {requestUri}");

    public void OnRestApiRequestFailed(string requestUri, in RestApiRequestResultError errors) =>
        _log.Report($"REST failed: {errors.Reason}, HTTP {errors.StatusCode}, code {errors.ExchangeCode} {errors.ExchangeMessage}, {requestUri}");

    public void OnRestApiRequestCanceled(string? requestUri, in RestApiRequestResultError errors) =>
        _log.Report($"REST canceled: {requestUri}");

    public void OnRestApiRequestDroppedByTtl(string? requestUri, in RestApiRequestResultError errors) =>
        _log.Report($"REST dropped by TTL: {requestUri}");

    public void OnRestApiRequestRetryAttempt(string? requestUri, in RestApiRequestResultError errors, in RestApiRequestResultResiliency resiliency) =>
        _log.Report($"REST retry {resiliency.RetryNumber}: {errors.Reason}, HTTP {errors.StatusCode}, code {errors.ExchangeCode}, {requestUri}");

    public void OnRestApiRequestRejected(in RestApiRequestResultError errors) =>
        _log.Report($"REST rejected before sending: rule {errors.RuleViolation}");

    // WebSocket API

    public void OnWsApiConnected(in WsApiConnectionInfo connection) =>
        _log.Report($"WS API connected: {connection.Uri}, lane {connection.Lane}, epoch {connection.Epoch}");

    public void OnWsApiDisconnected(in WsApiConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        _log.Report($"WS API disconnected: {connection.Uri}, lane {connection.Lane}, {disconnect.Reason}");

    public void OnWsApiConnectFailed(in WsApiConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        _log.Report($"WS API connect failed, attempt {attempt}: {connection.Uri}, {failure.Reason}, HTTP {failure.StatusCode} {failure.Fault?.Message}");

    public void OnWsApiResponseUnmatched(in WsApiConnectionInfo connection, long requestId) =>
        _log.Report($"WS API response unmatched: {connection.Uri}, request {requestId}");

    public void OnWsApiMessageUnknown(in WsApiConnectionInfo connection) =>
        _log.ReportContractBreak($"WS API message unknown: {connection.Uri}, lane {connection.Lane}");

    public void OnWsApiMessageNonStandard(in WsApiConnectionInfo connection) =>
        _log.ReportContractBreak($"WS API message non-standard: {connection.Uri}, lane {connection.Lane}");

    public void OnWsApiRequestSucceeded(Uri uri, string method, long requestId) =>
        _log.Report($"WS API ok: {method}, request {requestId}, {uri}");

    public void OnWsApiRequestFailed(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Report($"WS API failed: {method}, {errors.Reason}, HTTP {errors.StatusCode}, code {errors.ExchangeCode} {errors.ExchangeMessage}, request {errors.RequestId}");

    public void OnWsApiRequestCanceled(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Report($"WS API canceled: {method}, request {errors.RequestId}");

    public void OnWsApiRequestDroppedByTtl(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Report($"WS API dropped by TTL: {method}, {errors.Reason}");

    public void OnWsApiRequestRetryAttempt(Uri uri, string method, in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency) =>
        _log.Report($"WS API retry {resiliency.RetryNumber}: {method}, {errors.Reason}, code {errors.ExchangeCode}");

    public void OnWsApiRequestRejected(Uri uri, string method, in WsApiRequestResultError errors) =>
        _log.Report($"WS API rejected before sending: {method}, rule {errors.RuleViolation}");

    // Streams

    public void OnWsStreamConnected(in WsStreamConnectionInfo connection) =>
        _log.Report($"Stream connected: {connection.Uri}, shard {connection.Shard}, epoch {connection.Epoch}");

    public void OnWsStreamDisconnected(in WsStreamConnectionInfo connection, in WsDisconnectInfo disconnect) =>
        _log.Report($"Stream disconnected: {connection.Uri}, shard {connection.Shard}, {disconnect.Reason}");

    public void OnWsStreamConnectFailed(in WsStreamConnectionInfo connection, in WsDisconnectInfo failure, int attempt) =>
        _log.Report($"Stream connect failed, attempt {attempt}: {connection.Uri}, {failure.Reason}, HTTP {failure.StatusCode} {failure.Fault?.Message}");

    public void OnWsStreamReplyUnmatched(in WsStreamConnectionInfo connection, long requestId) =>
        _log.Report($"Stream reply unmatched: {connection.Uri}, request {requestId}");

    public void OnWsStreamMessageUnknown(in WsStreamConnectionInfo connection) =>
        _log.ReportContractBreak($"Stream message unknown: {connection.Uri}, shard {connection.Shard}");

    public void OnWsStreamMessageNonStandard(in WsStreamConnectionInfo connection) =>
        _log.ReportContractBreak($"Stream message non-standard: {connection.Uri}, shard {connection.Shard}");

    // Watchdog

    public void OnWatchdogSilent(long silenceMillis) =>
        _log.Report($"Watchdog silent for {silenceMillis} ms");

    public void OnWatchdogResumed(in WsStreamGap gap) =>
        _log.Report($"Watchdog resumed after {gap.EndUtcMillis - gap.StartUtcMillis} ms, {gap.Reason}");

    public void OnWatchdogConnectFailed(in WsDisconnectInfo failure, int attempt) =>
        _log.Report($"Watchdog connect failed, attempt {attempt}: {failure.Reason}, HTTP {failure.StatusCode} {failure.Fault?.Message}");
}
