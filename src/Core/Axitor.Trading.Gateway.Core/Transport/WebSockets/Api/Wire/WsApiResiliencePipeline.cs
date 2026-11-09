using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>WebSocket API resilience pipeline of one market.</summary>
internal sealed class WsApiResiliencePipeline<TDialect, TLimiter, TEventDispatcher, TTelemetry> : IDisposable
    where TDialect : struct, IExchangeDialectWsApi
    where TLimiter : struct, IRateLimiter
    where TEventDispatcher : struct, IWsApiEventDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    // Owned by the client, shared admission.
    private readonly BanGate _banGate;
    private readonly NetworkGate _networkGate;
    private readonly TLimiter _limiter;

    // Owned by the pipeline.
    private readonly WsApiTransport<TDialect, TLimiter, TEventDispatcher, TTelemetry> _transport;
    private readonly TTelemetry _telemetry;
    private readonly Uri _uri;

    private readonly bool _captureErrorMessageString;
    private readonly bool _captureErrorDataString;

    // Retry policy
    private readonly int _maxJitterMillisOnNetworkRepaired;
    private readonly int _maxJitterMillisOnRateLimits;
    private readonly int _maxJitterMillisOnRetry;
    private readonly int _minJitterMillisOnRetry;
    private readonly int _retryBaseDelayMillis;
    private readonly int _retryMaxDelayMillis;
    private readonly int _retryFallbackDelayMillis;

    /// <summary>Creates a pipeline and its transport. Nothing connects until Start. A single lane serves Critical requests too; the hook prepares every new connection.</summary>
    public WsApiResiliencePipeline(
        WsApiTransportOptions options,
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter,
        TEventDispatcher dispatcher,
        TTelemetry telemetry,
        BanGate banGate,
        NetworkGate networkGate,
        NetworkRepairWorker networkRepairWorker,
        IWsApiConnectionHook? hook,
        bool singleLane)
    {
        _banGate = banGate;
        _networkGate = networkGate;
        _limiter = limiter;
        _telemetry = telemetry;
        _uri = uri;

        _transport = new WsApiTransport<TDialect, TLimiter, TEventDispatcher, TTelemetry>(
            options, uri, messagesPerSecond, connectLimitBucket, connectWeight,
            limiter, dispatcher, telemetry, banGate, networkGate, networkRepairWorker, hook, singleLane);

        _captureErrorMessageString = options.Resiliency.CaptureErrorMessageString;
        _captureErrorDataString = options.Resiliency.CaptureErrorDataString;

        _maxJitterMillisOnNetworkRepaired = options.Resiliency.MaxJitterMillisOnNetworkRepaired;
        _maxJitterMillisOnRateLimits = options.Resiliency.MaxJitterMillisOnRateLimits;
        _maxJitterMillisOnRetry = options.Resiliency.MaxJitterMillisOnRetry;
        _minJitterMillisOnRetry = options.Resiliency.MinJitterMillisOnRetry;
        _retryBaseDelayMillis = options.Resiliency.RetryBaseDelayMillis;
        _retryMaxDelayMillis = options.Resiliency.RetryMaxDelayMillis;
        _retryFallbackDelayMillis = options.Resiliency.RetryFallbackDelayMillis;
    }

    /// <summary>Completes on app shutdown or Dispose; faults with WsApiFatalException once the transport is fatal.</summary>
    public Task Completion => _transport.Completion;

    /// <summary>Starts the transport. Connections open in the background. Call once.</summary>
    public void Start(CancellationToken appCancellationToken) => _transport.Start(appCancellationToken);

    /// <summary>The open connection the priority would use now. False when none is open.</summary>
    public bool TryGetConnection(RequestPriority priority, out WsApiConnectionTicket ticket) => _transport.TryGetConnection(priority, out ticket);

    /// <summary>Cold path. Waits until a connection the priority can use is open. False when the deadline passed first; a zero deadline waits for good. Throws once the transport is fatal.</summary>
    public ValueTask<bool> WaitConnectedAsync(RequestPriority priority, long deadlineTicks, CancellationToken cancellationToken) =>
        _transport.WaitConnectedAsync(priority, deadlineTicks, cancellationToken);

    /// <summary>Throws WsApiFatalException once the transport is fatal.</summary>
    public void ThrowIfFatal() => _transport.ThrowIfFatal();

    /// <summary>Cold path. Stops the transport for good, e.g. when a handler threw outside the receive loop.</summary>
    public void EnterFatal(in WsDisconnectInfo info) => _transport.EnterFatal(in info);

    /// <summary>WebSocket API call. Throws WsApiFatalException once the transport is fatal.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<WsApiRequestResult<TValue>> SendAsync<TRequest, TAuth, TParser, TValue>(
        WsApiEndpointDescriptor descriptor,
        TRequest request,
        TAuth auth,
        CancellationToken cancellationToken)
        where TRequest : struct, IWsApiRequestWriter
        where TAuth : struct, IWsApiAuthenticator
        where TParser : struct, IWsApiResultParser<TValue>
    {
        long startTimerFullTicks = SysTime.GetTimestamp();
        long deadlineTicks = 0 < descriptor.TTL ? startTimerFullTicks + SysTime.TicksFromMillis(descriptor.TTL) : 0;
        WsApiRequestFrame<TDialect, TRequest, TAuth> frame = new(request, auth);
        WsApiRequestResult<TValue> result = default;

        // Retry count is limited strictly by the request TTL, avoiding arbitrary counter state.
        while (true)
        {
            try
            {
                while (_banGate.IsBanned(out long waitBanMillis))
                    await Task.Delay((int)(waitBanMillis + GetRetryJitterMillis()), cancellationToken).ConfigureAwait(false);

                if (!_networkGate.IsHealthy() && await _networkGate.WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false))
                    await Task.Delay(WsReconnectPolicy.GetJitterMillis(0, _maxJitterMillisOnNetworkRepaired), cancellationToken).ConfigureAwait(false);

                // Nothing is sent yet, so waiting for a lane is not a retry. No jitter: the first order after a reconnect goes at once.
                if (!_transport.HasConnection(descriptor.Priority)
                    && !await _transport.WaitConnectedAsync(descriptor.Priority, deadlineTicks, cancellationToken).ConfigureAwait(false))
                {
                    DropByTtl(ref result, descriptor.Method, startTimerFullTicks);
                    return result;
                }

                while (!_limiter.TryReserve(descriptor.LimitBucket, descriptor.Weight, out long waitMillis))
                    await Task.Delay((int)(waitMillis + WsReconnectPolicy.GetJitterMillis(0, _maxJitterMillisOnRateLimits)), cancellationToken).ConfigureAwait(false);

                // TTL check at the I/O boundary, after every wait.
                if (IsExpiredTtl(startTimerFullTicks, descriptor.TTL))
                {
                    DropByTtl(ref result, descriptor.Method, startTimerFullTicks);
                    return result;
                }

                long startTimerTransportTicks = SysTime.GetTimestamp();
                WsApiTransportResult attempt = await _transport.SendAsync(descriptor.Priority, frame, cancellationToken).ConfigureAwait(false);
                ReadAttempt<TParser, TValue>(in attempt, descriptor.Market, startTimerTransportTicks, ref result);

                // Hot path for success.
                if (result.Outcome == RequestOutcome.Success)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    _telemetry.OnWsApiRequestSucceeded(_uri, descriptor.Method, result.RequestId);
                    return result;
                }

                // The app is shutting down: the lanes ended every request in flight.
                if (result.Outcome == RequestOutcome.Canceled)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    _telemetry.OnWsApiRequestCanceled(_uri, descriptor.Method, in result.Errors);
                    return result;
                }

                // Cold path for failures.
                _telemetry.OnWsApiRequestFailed(_uri, descriptor.Method, in result.Errors);

                // The transport went fatal while the request was in flight: the caller gets the fatal, not a lost connection.
                _transport.ThrowIfFatal();

                result.Resiliency.Action = ClassifyFailureReasonAction(in result.Errors, descriptor.Market, descriptor.IsRetryable);

                bool isBanFault = result.Errors.Reason == WsApiRequestErrorReason.ExchangeApiBanShort;
                bool waitsForConnection = result.Errors.Reason is WsApiRequestErrorReason.NotSent
                    or WsApiRequestErrorReason.NoConnection
                    or WsApiRequestErrorReason.ConnectionLost;

                // Any 429 bans the whole client, whether the request is retried or not.
                if (isBanFault)
                {
                    result.Resiliency.RetryDelayMillis = 0 < result.Errors.RetryAfterMillis ? result.Errors.RetryAfterMillis : _retryFallbackDelayMillis;
                    _banGate.SetBanMillis(result.Resiliency.RetryDelayMillis);
                }

                // Skip. Any skipped error returns the complete failure breakdown in the result.
                if (result.Resiliency.Action == RequestErrorAction.Skip)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    return result;
                }

                // Fatal. The transport stops for good; a long ban stays on the shared gate.
                if (result.Resiliency.Action == RequestErrorAction.Fatal)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    _transport.EnterFatal(in result.Errors, in result.Resiliency);
                    ThrowFatal(in result.Errors, in result.Resiliency);
                }

                // Retry. The ban gate and the lane wait cover their own faults; the rest backs off.
                result.Resiliency.RetryNumber++;

                if (waitsForConnection)
                {
                    result.Resiliency.RetryDelayMillis = 0;
                }
                else if (!isBanFault)
                {
                    result.Resiliency.RetryDelayMillis = GetRetryDelayMillis(result.Resiliency.RetryNumber);
                }

                _telemetry.OnWsApiRequestRetryAttempt(_uri, descriptor.Method, in result.Errors, in result.Resiliency);

                if (!waitsForConnection && !isBanFault)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(result.Resiliency.RetryDelayMillis), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is reserved for host teardown: return a clean Canceled outcome.
                result.Errors.Reason = WsApiRequestErrorReason.CancellationByCaller;
                result.Outcome = RequestOutcome.Canceled;
                SetLatencyData(ref result.Latency, startTimerFullTicks);
                _telemetry.OnWsApiRequestCanceled(_uri, descriptor.Method, in result.Errors);
                return result;
            }
        }
    }

    /// <summary>
    /// Cold path. Sends one request strictly on the ticket's connection: no retries and no waiting for another connection, e.g. from a connection hook.
    /// Waits out a ban and the rate limit. Throws WsApiFatalException once the transport is fatal.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<WsApiRequestResult<TValue>> SendOnAsync<TRequest, TAuth, TParser, TValue>(
        WsApiConnectionTicket ticket,
        WsApiEndpointDescriptor descriptor,
        TRequest request,
        TAuth auth,
        CancellationToken cancellationToken)
        where TRequest : struct, IWsApiRequestWriter
        where TAuth : struct, IWsApiAuthenticator
        where TParser : struct, IWsApiResultParser<TValue>
    {
        long startTimerFullTicks = SysTime.GetTimestamp();
        WsApiRequestFrame<TDialect, TRequest, TAuth> frame = new(request, auth);
        WsApiRequestResult<TValue> result = default;

        try
        {
            while (_banGate.IsBanned(out long waitBanMillis))
                await Task.Delay((int)(waitBanMillis + GetRetryJitterMillis()), cancellationToken).ConfigureAwait(false);

            while (!_limiter.TryReserve(descriptor.LimitBucket, descriptor.Weight, out long waitMillis))
                await Task.Delay((int)(waitMillis + WsReconnectPolicy.GetJitterMillis(0, _maxJitterMillisOnRateLimits)), cancellationToken).ConfigureAwait(false);

            if (IsExpiredTtl(startTimerFullTicks, descriptor.TTL))
            {
                DropByTtl(ref result, descriptor.Method, startTimerFullTicks);
                return result;
            }

            long startTimerTransportTicks = SysTime.GetTimestamp();
            WsApiTransportResult attempt = await _transport.SendOnAsync(ticket, frame, cancellationToken).ConfigureAwait(false);
            ReadAttempt<TParser, TValue>(in attempt, descriptor.Market, startTimerTransportTicks, ref result);
            SetLatencyData(ref result.Latency, startTimerFullTicks);

            if (result.Outcome == RequestOutcome.Success)
            {
                _telemetry.OnWsApiRequestSucceeded(_uri, descriptor.Method, result.RequestId);
                return result;
            }

            if (result.Outcome == RequestOutcome.Canceled)
            {
                _telemetry.OnWsApiRequestCanceled(_uri, descriptor.Method, in result.Errors);
                return result;
            }

            _telemetry.OnWsApiRequestFailed(_uri, descriptor.Method, in result.Errors);
            _transport.ThrowIfFatal();

            // Never retried here: the caller decides what a failure on this connection means.
            result.Resiliency.Action = ClassifyFailureReasonAction(in result.Errors, descriptor.Market, isRetryable: false);

            if (result.Errors.Reason == WsApiRequestErrorReason.ExchangeApiBanShort)
            {
                result.Resiliency.RetryDelayMillis = 0 < result.Errors.RetryAfterMillis ? result.Errors.RetryAfterMillis : _retryFallbackDelayMillis;
                _banGate.SetBanMillis(result.Resiliency.RetryDelayMillis);
            }

            if (result.Resiliency.Action == RequestErrorAction.Fatal)
            {
                _transport.EnterFatal(in result.Errors, in result.Resiliency);
                ThrowFatal(in result.Errors, in result.Resiliency);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is reserved for host teardown: return a clean Canceled outcome.
            result.Errors.Reason = WsApiRequestErrorReason.CancellationByCaller;
            result.Outcome = RequestOutcome.Canceled;
            SetLatencyData(ref result.Latency, startTimerFullTicks);
            _telemetry.OnWsApiRequestCanceled(_uri, descriptor.Method, in result.Errors);
            return result;
        }
    }

    /// <summary>Cold path. Fails a request refused before sending and reports it. Nothing is sent.</summary>
    public WsApiRequestResult<TValue> Reject<TValue>(string method, WsApiRequestErrorReason reason, int ruleViolation)
    {
        WsApiRequestResult<TValue> result = new()
        {
            Outcome = RequestOutcome.Failed,
            Errors = new WsApiRequestResultError() { Reason = reason, RuleViolation = ruleViolation },
            Resiliency = new WsApiRequestResultResiliency() { Action = RequestErrorAction.Skip },
        };

        _telemetry.OnWsApiRequestRejected(_uri, method, in result.Errors);
        return result;
    }

    /// <summary>Turns one transport attempt into the request result and releases the slot. Keeps the last error on success.</summary>
    private void ReadAttempt<TParser, TValue>(in WsApiTransportResult attempt, int market, long startTimerTransportTicks, ref WsApiRequestResult<TValue> result)
        where TParser : struct, IWsApiResultParser<TValue>
    {
        result.RequestId = attempt.RequestId;
        result.Latency.TransportNetworkOnly = 0;
        result.Latency.TransportParsingOnly = 0;

        WsApiPendingSlot? slot = attempt.Slot;

        if (slot is not null)
        {
            try
            {
                if (attempt.Completion.Outcome == WsApiPendingOutcome.Response)
                {
                    result.Latency.TransportNetworkOnly = SysTime.NanosFromTicks(slot.ReceivedTicks - slot.SentTicks);
                    ReadResponse<TParser, TValue>(slot.Message, market, attempt.RequestId, ref result);
                }
                else
                {
                    SetAttemptError(ref result, in attempt);
                }
            }
            finally
            {
                slot.Release();
            }
        }
        else
        {
            SetAttemptError(ref result, in attempt);
        }

        result.Latency.TransportFull = SysTime.ElapsedNanos(startTimerTransportTicks);
    }

    /// <summary>Reads the envelope, then the result when the response is a success.</summary>
    private void ReadResponse<TParser, TValue>(ReadOnlySpan<byte> message, int market, long requestId, ref WsApiRequestResult<TValue> result)
        where TParser : struct, IWsApiResultParser<TValue>
    {
        long startTimerParsingTicks = SysTime.GetTimestamp();

        TDialect.ReadResponse(_limiter, message, market, _captureErrorMessageString, _captureErrorDataString, out WsApiResponseEnvelope envelope);

        if (envelope.Reason == WsApiRequestErrorReason.None)
        {
            try
            {
                result.Value = TParser.Parse(message.Slice(envelope.ResultStart, envelope.ResultLength));
                result.Outcome = RequestOutcome.Success;
            }
            catch (JsonException)
            {
                // A response with our id that cannot be read: the request may have been applied.
                envelope.Reason = WsApiRequestErrorReason.ExceptionJson;
            }
        }

        if (envelope.Reason != WsApiRequestErrorReason.None)
        {
            result.Outcome = RequestOutcome.Failed;
            result.Errors = new WsApiRequestResultError()
            {
                Reason = envelope.Reason,
                RequestId = requestId,
                StatusCode = envelope.StatusCode,
                ExchangeCode = envelope.ExchangeCode,
                ExchangeMessage = envelope.ExchangeMessage,
                ExchangeData = envelope.ExchangeData,
                RetryAfterMillis = envelope.RetryAfterMillis,
            };
        }

        result.Latency.TransportParsingOnly = SysTime.ElapsedNanos(startTimerParsingTicks);
    }

    /// <summary>An attempt that ended without a response.</summary>
    private static void SetAttemptError<TValue>(ref WsApiRequestResult<TValue> result, in WsApiTransportResult attempt)
    {
        WsApiPendingOutcome outcome = attempt.Completion.Outcome;

        result.Outcome = outcome == WsApiPendingOutcome.AppShutdown ? RequestOutcome.Canceled : RequestOutcome.Failed;
        result.Errors = new WsApiRequestResultError()
        {
            Reason = ToReason(outcome),
            RequestId = attempt.RequestId,
            Disconnect = new WsDisconnectInfo() { Reason = attempt.Completion.Reason },
        };
    }

    private static WsApiRequestErrorReason ToReason(WsApiPendingOutcome outcome) =>
        outcome switch
        {
            WsApiPendingOutcome.NotSent => WsApiRequestErrorReason.NotSent,
            WsApiPendingOutcome.NoConnection => WsApiRequestErrorReason.NoConnection,
            WsApiPendingOutcome.InFlightLimit => WsApiRequestErrorReason.InFlightLimit,
            WsApiPendingOutcome.Timeout => WsApiRequestErrorReason.Timeout,
            WsApiPendingOutcome.AppShutdown => WsApiRequestErrorReason.CancellationByCaller,
            _ => WsApiRequestErrorReason.ConnectionLost,
        };

    /// <summary>Classifies a failure reason to resiliency action.</summary>
    private static RequestErrorAction ClassifyFailureReasonAction(in WsApiRequestResultError errors, int market, bool isRetryable)
    {
        switch (errors.Reason)
        {
            case WsApiRequestErrorReason.CancellationByCaller:
            case WsApiRequestErrorReason.InFlightLimit:
            case WsApiRequestErrorReason.ExceptionJson:
            case WsApiRequestErrorReason.StatusCodeOtherError:
            case WsApiRequestErrorReason.ExchangeApiRequestPartial:
            case WsApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced:
            case WsApiRequestErrorReason.ExchangeApiTimestampRejected:
                return RequestErrorAction.Skip;

            case WsApiRequestErrorReason.ConnectionFatal:
            case WsApiRequestErrorReason.StatusCodeNotFound:
            case WsApiRequestErrorReason.ExchangeApiUnauthorized:
            case WsApiRequestErrorReason.ExchangeApiRequestMalformed:
            case WsApiRequestErrorReason.ExchangeApiBanLong:
                return RequestErrorAction.Fatal;

            case WsApiRequestErrorReason.ExchangeApiRequestError:
                return TDialect.ClassifyExchangeCodeAction(errors.ExchangeCode, market, isRetryable);

            // NotSent, NoConnection, Timeout, ConnectionLost, StatusUnknown, RequestFailed, BanShort.
            default:
                return isRetryable ? RequestErrorAction.Retry : RequestErrorAction.Skip;
        }
    }

    private void DropByTtl<TValue>(ref WsApiRequestResult<TValue> result, string method, long startTimerFullTicks)
    {
        result.Errors.WasDroppedByTtl = true;
        result.Outcome = RequestOutcome.Failed;
        SetLatencyData(ref result.Latency, startTimerFullTicks);
        _telemetry.OnWsApiRequestDroppedByTtl(_uri, method, in result.Errors);
    }

    /// <summary>Exponential back-off plus jitter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetRetryDelayMillis(int retryNumber) =>
        WsReconnectPolicy.GetBackoffDelayMillis(retryNumber, _retryBaseDelayMillis, _retryMaxDelayMillis) + GetRetryJitterMillis();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetRetryJitterMillis() => WsReconnectPolicy.GetJitterMillis(_minJitterMillisOnRetry, _maxJitterMillisOnRetry);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpiredTtl(long startTimerFullTicks, long ttlMillis)
    {
        if (ttlMillis <= 0) return false;
        return SysTime.TicksFromMillis(ttlMillis) <= SysTime.GetTimestamp() - startTimerFullTicks;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetLatencyData(ref WsApiRequestResultLatency latency, long startTimerFull)
    {
        latency.ExecutionFull = SysTime.ElapsedNanos(startTimerFull);
        latency.ExecutionResiliencyOnly = latency.ExecutionFull - latency.TransportFull;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFatal(in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency) =>
        throw new WsApiFatalException(in errors, in resiliency);

    /// <summary>Stops the transport and releases the sockets.</summary>
    public void Dispose() => _transport.Dispose();
}
