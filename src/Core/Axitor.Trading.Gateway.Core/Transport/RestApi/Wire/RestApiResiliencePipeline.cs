using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Wire;

/// <summary>REST API resilience pipeline.</summary>
internal sealed class RestApiResiliencePipeline<TTelemetry> : IDisposable where TTelemetry : struct, ITelemetryHandler
{
    // Owns by the client, shared admission.
    private readonly BanGate _banGate;
    private readonly NetworkGate _networkGate;
    private readonly NetworkRepairWorker _networkRepairWorker;

    // Slots
    private readonly SemaphoreSlim _normalSemaphoreSlots;
    private readonly SemaphoreSlim _criticalSemaphoreSlots;

    // Owns by the pipeline.
    private readonly RestApiTransport _restApiTransport;
    private readonly TTelemetry _telemetry;

    // Retry policy 
    private readonly int _maxJitterMillisOnNetworkRepaired;
    private readonly int _maxJitterMillisOnRateLimits;
    private readonly int _maxJitterMillisOnRetry;
    private readonly int _minJitterMillisOnRetry;
    private readonly int _retryBaseDelayMillis;
    private readonly int _retryMaxDelayMillis;
    private readonly int _retryFallbackDelayMillis;

    /// <summary>Creates a pipeline.</summary>
    public RestApiResiliencePipeline(EngineOptions options, string? apiKey, string apiKeyHeader, TTelemetry telemetry, HttpMessageHandler? messageHandler, BanGate banGate, NetworkGate networkGate, NetworkRepairWorker networkRepairWorker)
    {
        _telemetry = telemetry;
        _banGate = banGate;
        _networkGate = networkGate;
        _networkRepairWorker = networkRepairWorker;
        _normalSemaphoreSlots = new SemaphoreSlim(options.RestApi.Throttle.NormalConcurrencySlots, options.RestApi.Throttle.NormalConcurrencySlots);
        _criticalSemaphoreSlots = new SemaphoreSlim(options.RestApi.Throttle.CriticalConcurrencySlots, options.RestApi.Throttle.CriticalConcurrencySlots);
        _restApiTransport = new RestApiTransport(options, apiKey, apiKeyHeader, messageHandler);

        _maxJitterMillisOnNetworkRepaired = options.RestApi.Resiliency.MaxJitterMillisOnNetworkRepaired;
        _maxJitterMillisOnRateLimits = options.RestApi.Resiliency.MaxJitterMillisOnRateLimits;
        _maxJitterMillisOnRetry = options.RestApi.Resiliency.MaxJitterMillisOnRetry;
        _minJitterMillisOnRetry = options.RestApi.Resiliency.MinJitterMillisOnRetry;
        _retryBaseDelayMillis = options.RestApi.Resiliency.RetryBaseDelayMillis;
        _retryMaxDelayMillis = options.RestApi.Resiliency.RetryMaxDelayMillis;
        _retryFallbackDelayMillis = options.RestApi.Resiliency.RetryFallbackDelayMillis;
    }

    /// <summary>Acquire semaphore slot.</summary>
    private async ValueTask<SemaphoreSlim> SemaphoreSlotAcquireAsync(bool isCritical, CancellationToken cancellationToken)
    {
        if (isCritical)
        {
            if (_normalSemaphoreSlots.Wait(0, cancellationToken)) return _normalSemaphoreSlots;

            await _criticalSemaphoreSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _criticalSemaphoreSlots;
        }

        await _normalSemaphoreSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _normalSemaphoreSlots;
    }

    /// <summary>REST API call.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<RestApiRequestResult<TValue>> SendAsync<TPayload, TAuth, TLimiter, TParser, TValue, TDialect>(
        RestApiEndpointDescriptor descriptor,
        TPayload payload,
        TAuth auth,
        TLimiter rateLimiter,
        TParser parser,
        CancellationToken cancellationToken)
        where TPayload : struct, IPayloadWriter
        where TAuth : struct, IRequestAuthenticator
        where TParser : struct, IResponseParser<TValue>
        where TDialect : struct, IExchangeDialectRestApi
        where TLimiter : struct, IRateLimiter
    {
        long startTimerFullTicks = SysTime.GetTimestamp();
        RestApiRequestResult<TValue> result = default;
        RestApiRequestResult<TValue> attempt = default;

        // Retry count is limited strictly by the request TTL, avoiding arbitrary counter state.
        while (true)
        {
            try
            {
                // Checking network and limits before acquiring semaphore slots and add a jitter to prevent thundering herd simultaneous requests.
                while (_banGate.IsBanned(out long waitBanMillis))
                    await Task.Delay((int)(waitBanMillis + JitterRetryDelayMillis()), cancellationToken).ConfigureAwait(false);

                if (!_networkGate.IsHealthy() && await _networkGate.WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false))
                    await Task.Delay(JitterDelayMillis(_maxJitterMillisOnNetworkRepaired), cancellationToken).ConfigureAwait(false);

                while (!rateLimiter.TryReserve(descriptor.LimitBucket, descriptor.Weight, out long waitMillis))
                    await Task.Delay((int)(waitMillis + JitterDelayMillis(_maxJitterMillisOnRateLimits)), cancellationToken).ConfigureAwait(false);

                // Acquire semaphore slot.
                SemaphoreSlim activeSemaphoreSlim = await SemaphoreSlotAcquireAsync(descriptor.Priority == RequestPriority.Critical, cancellationToken).ConfigureAwait(false);

                try
                {
                    // TTL check positioned at the I/O boundary: edge-case expirations do not justify redundant branching across the hot path.
                    // DESIGN NOTE: RequestUri may be null on the first attempt to prevent upfront string allocation and cryptographic signing overhead for dropped requests.
                    if (IsExpiredTtl(startTimerFullTicks, descriptor.TTL))
                    {
                        result.Errors.WasDroppedByTtl = true;
                        result.Outcome = RequestOutcome.Failed;
                        SetLatencyData(ref result.Latency, startTimerFullTicks);
                        _telemetry.OnRestApiRequestDroppedByTtl(result.Errors.RequestUri, in result.Errors);
                        return result;
                    }

                    // REST API call.
                    attempt = await _restApiTransport.SendAsync<TPayload, TAuth, TLimiter, TParser, TValue, TDialect, TTelemetry>(descriptor, payload, auth, rateLimiter, parser, _telemetry, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    activeSemaphoreSlim.Release();
                }

                // Merge result and attempt to keep retry and error data.
                MergeAttempt(ref result, in attempt);

                // Hot path for success.
                if (result.Outcome == RequestOutcome.Success)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    return result;
                }

                // Cold path for failures.
                result.Resiliency.Action = ClassifyFailureReasonAction<TDialect>(in result.Errors, descriptor.Market, descriptor.IsRetryable);

                // Retry handling
                bool isNetworkFault = IsNetworkFault(result.Errors.Reason);
                bool isBanFault = result.Errors.StatusCode == 429 || result.Errors.Reason == RestApiRequestErrorReason.ExchangeApiBanShort;

                // Set ban period on HTTP 429 Too Many Requests
                if (isBanFault)
                {
                    result.Resiliency.RetryDelayMillis = 0 < result.Headers.RetryAfterSeconds ? result.Headers.RetryAfterSeconds * 1000L : _retryFallbackDelayMillis;
                    _banGate.SetBanMillis(result.Resiliency.RetryDelayMillis);
                }

                // Run repair worker on network failure.
                if (isNetworkFault)
                {
                    result.Resiliency.RetryDelayMillis = 0;
                    if (_networkGate.MarkDown()) _networkRepairWorker.StartRepairLoop(cancellationToken);
                }

                // Skip. Any skipped error gracefully returns the complete failure breakdown in the result structure for further analysis.
                if (result.Resiliency.Action == RequestErrorAction.Skip)
                {
                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    return result;
                }

                // Fatal. Security/ban violations.
                // DESIGN NOTE: Strict fail-fast policy for unrecoverable errors.
                // Statuses like 401 (Unauthorized), 404 (Not Found), malformed payloads (41x),
                // and long - term bans (403 / 418) cannot be resolved by the engine's internal retry mechanisms. 
                // Retrying these is either useless or actively harmful (e.g., extending a ban).
                // Therefore, they bypass resiliency loops and trigger an immediate fatal exception. 
                // If the host application wishes to catch this exception and keep the process alive,
                // that is strictly their responsibility.
                if (result.Resiliency.Action == RequestErrorAction.Fatal)
                {
                    // The ban stays in force even if the caller swallows the exception.
                    if (result.Errors.Reason == RestApiRequestErrorReason.ExchangeApiBanLong)
                    {
                        _banGate.SetBanMillis(0 < result.Headers.RetryAfterSeconds ? result.Headers.RetryAfterSeconds * 1000L : _retryFallbackDelayMillis);
                    }

                    SetLatencyData(ref result.Latency, startTimerFullTicks);
                    throw new RestApiFatalException(result.Errors, result.Resiliency);
                }

                // Retry. Transient failures, e.g. network/transport faults and specific temporary exchange outages.
                // To prevent duplicate executions, limits auto-retries for order-related operations to idempotent requests.

                result.Resiliency.RetryNumber++;
                if (!isNetworkFault && !isBanFault) result.Resiliency.RetryDelayMillis = GetRetryDelayMillis(result.Resiliency.RetryNumber, result.Headers.RetryAfterSeconds);
                _telemetry.OnRestApiRequestRetryAttempt(result.Errors.RequestUri, in result.Errors, in result.Resiliency);

                // Await delay (except ban and network failures)
                if (!isNetworkFault && !isBanFault && result.Resiliency.RetryDelayMillis != 0) await Task.Delay(TimeSpan.FromMilliseconds(result.Resiliency.RetryDelayMillis), cancellationToken).ConfigureAwait(false);
            }

            // Cancellation of a request. Caller's token, not an error — the pipeline stops the cycle.
            catch (OperationCanceledException)
            {
                // DESIGN NOTE: RequestUri may be null to prevent upfront string allocation and cryptographic signing overhead for canceled requests.
                // DESIGN NOTE: In this engine, cancellation is strictly reserved for global host teardown. Catch it here solely to ensure a graceful
                // shutdown without unhandled task exceptions, returning a clean Canceled outcome to the dying executor.

                result.Errors.Reason = RestApiRequestErrorReason.CancellationByCaller;
                result.Outcome = RequestOutcome.Canceled;
                SetLatencyData(ref result.Latency, startTimerFullTicks);
                _telemetry.OnRestApiRequestCanceled(result.Errors.RequestUri, in result.Errors);
                return result;
            }
        }
    }

    /// <summary>A context-aware retry delay based on exchange headers and error classification.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetRetryDelayMillis(int retryNumber, int retryAfterSeconds)
    {
        // Authoritative exchange header: strictly obey, never clamp below exchange demand.
        if (0 < retryAfterSeconds)
            return (retryAfterSeconds * 1000) + JitterRetryDelayMillis();

        // Exponential back-off delay.
        return BackoffRetryDelayMillis(retryNumber, _retryBaseDelayMillis, _retryMaxDelayMillis) + JitterRetryDelayMillis();
    }

    /// <summary>A jitter for delay.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int JitterDelayMillis(int maxJitterMillis)
    {
        return Random.Shared.Next(0, maxJitterMillis);
    }

    /// <summary>A jitter for retry delay.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int JitterRetryDelayMillis()
    {
        return Random.Shared.Next(_minJitterMillisOnRetry, _maxJitterMillisOnRetry);
    }

    /// <summary>A progressive back-off retry delay.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long BackoffRetryDelayMillis(int retryNumber, int baseMillis, int maxMillis = 0)
    {
        int shift = Math.Clamp(retryNumber - 1, 0, 16);
        long delay = (long)baseMillis << shift;
        return maxMillis <= 0 || delay < maxMillis ? delay : (long)maxMillis;
    }

    /// <summary>Check is ttl expired.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpiredTtl(long startTimerFullTicks, long ttlMillis)
    {
        if (ttlMillis <= 0) return false;
        return SysTime.TicksFromMillis(ttlMillis) <= SysTime.GetTimestamp() - startTimerFullTicks;
    }

    /// <summary>Sets latency data.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetLatencyData(ref RestApiRequestResultLatency latency, long startTimerFull)
    {
        latency.ExecutionFull = SysTime.ElapsedNanos(startTimerFull);
        latency.ExecutionResiliencyOnly = latency.ExecutionFull - latency.TransportFull;
    }

    /// <summary>Checks network failure.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNetworkFault(RestApiRequestErrorReason reason) =>
        reason is RestApiRequestErrorReason.ExceptionSocket
               or RestApiRequestErrorReason.ExceptionHttp
               or RestApiRequestErrorReason.ExceptionIO;

    /// <summary>Classifies a failure reason to resiliency action.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RequestErrorAction ClassifyFailureReasonAction<TDialect>(in RestApiRequestResultError errors, int market, bool isRetryable) where TDialect : struct, IExchangeDialectRestApi
    {
        switch (errors.Reason)
        {
            case RestApiRequestErrorReason.CancellationByCaller:
            case RestApiRequestErrorReason.StatusCodeOtherError:
            case RestApiRequestErrorReason.ExceptionNull:
            case RestApiRequestErrorReason.ExceptionJson:
            case RestApiRequestErrorReason.ExchangeApiRequestPartial:
            case RestApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced:
            case RestApiRequestErrorReason.ExchangeApiTimestampRejected:
                return RequestErrorAction.Skip;

            case RestApiRequestErrorReason.StatusCodeNotFound:
            case RestApiRequestErrorReason.ExchangeApiUnauthorized:
            case RestApiRequestErrorReason.ExchangeApiRequestMalformed:
            case RestApiRequestErrorReason.ExchangeApiBanLong:
                return RequestErrorAction.Fatal;

            case RestApiRequestErrorReason.ExchangeApiRequestError:
                return TDialect.ClassifyExchangeCodeAction(errors.ExchangeCode, market, isRetryable);

            default:
                return isRetryable ? RequestErrorAction.Retry : RequestErrorAction.Skip;
        }
        ;
    }

    /// <summary>Moves one transport attempt into the request result. Keeps retry data, and keeps the last error on success.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MergeAttempt<TValue>(ref RestApiRequestResult<TValue> result, in RestApiRequestResult<TValue> attempt)
    {
        result.Outcome = attempt.Outcome;
        result.Headers = attempt.Headers;
        result.Latency.TransportFull = attempt.Latency.TransportFull;
        result.Latency.TransportNetworkOnly = attempt.Latency.TransportNetworkOnly;
        result.Latency.TransportParsingOnly = attempt.Latency.TransportParsingOnly;

        if (attempt.Outcome == RequestOutcome.Success)
        {
            result.Value = attempt.Value;
            return;
        }

        result.Errors = attempt.Errors;
    }

    /// <summary>Dispose IDisposable resources.</summary>
    public void Dispose()
    {
        _restApiTransport.Dispose();
        _normalSemaphoreSlots.Dispose();
        _criticalSemaphoreSlots.Dispose();
    }
}
