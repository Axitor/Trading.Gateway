using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.WsApi;

/// <summary>A spot user data session bound to its handler and its own WebSocket API connection.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Every new connection passes the hook before it serves, and the hook subscribes the account there while the user is subscribed.
/// On a rotation the new connection is subscribed before the old one drains, so no event falls between them.
/// One account may be subscribed on two connections at once (checked live, Oct 2026), which this needs.
/// </para>
/// <para>
/// DESIGN NOTE: The connection belongs to the session alone, so an active subscription on it can only be ours:
/// -2035 on subscribe and -2036 on unsubscribe count as success, and the subscription id is not kept.
/// </para>
/// <para>
/// DESIGN NOTE: The hook, subscribe, unsubscribe and the resubscribe loop take one control lock.
/// Requests go to the last connection that passed the hook: it is current, or becomes current right after the hook,
/// so a subscription never lands on a connection that is being replaced.
/// </para>
/// <para>
/// DESIGN NOTE: Subscribe answers only Subscribed or Rejected. Without an answer the exchange may or may not hold the subscription,
/// and a bot that loads its orders after such a maybe would miss events with no signal. So it asks again until the exchange answers.
/// A canceled subscribe is undone: no event reaches the handler.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore holds no unmanaged state without a wait handle; disposing it would make a late Release throw.")]
internal sealed class BinanceSpotUserDataSessionCore<THandler, TTelemetry> : BinanceSpotUserDataSession, IWsApiConnectionHook
    where THandler : struct, IBinanceSpotUserDataHandler
    where TTelemetry : struct, ITelemetryHandler
{
    // -2035 SUBSCRIPTION_ACTIVE: User Data Stream subscription already active.
    private const int SubscriptionActiveCode = -2035;

    // -2036 SUBSCRIPTION_INACTIVE: User Data Stream subscription not active.
    private const int SubscriptionInactiveCode = -2036;

    private readonly BinanceSpotUserDataState _state;
    private readonly SubscriptionEndpoint<TTelemetry> _subscription;
    private readonly THandler _handler;
    private readonly int _retryBaseDelayMillis;
    private readonly int _retryMaxDelayMillis;
    private readonly int _minJitterMillis;
    private readonly int _maxJitterMillis;
    private readonly CancellationToken _appToken;

    // One subscription change at a time: subscribe, unsubscribe, the hook and the resubscribe loop.
    private readonly SemaphoreSlim _controlLock = new(1, 1);

    private WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceSpotUserDataDispatcher<THandler>, TTelemetry>? _pipeline;

    // Last connection that passed the hook. Touched under the control lock.
    private WsApiConnectionTicket _prepared;

    public BinanceSpotUserDataSessionCore(
        BinanceSpotUserDataState state,
        SubscriptionEndpoint<TTelemetry> subscription,
        THandler handler,
        ResiliencyWsApiOptions resiliency,
        CancellationToken appToken)
    {
        _state = state;
        _subscription = subscription;
        _handler = handler;
        _retryBaseDelayMillis = resiliency.ReconnectBaseDelayMillis;
        _retryMaxDelayMillis = resiliency.ReconnectMaxDelayMillis;
        _minJitterMillis = resiliency.MinJitterMillisOnReconnect;
        _maxJitterMillis = resiliency.MaxJitterMillisOnReconnect;
        _appToken = appToken;
    }

    /// <inheritdoc />
    public override Task Completion => Pipeline.Completion;

    private WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceSpotUserDataDispatcher<THandler>, TTelemetry> Pipeline => _pipeline!;

    /// <summary>Cold path. Binds the pipeline before it starts and runs the resubscribe loop.</summary>
    public void Bind(WsApiResiliencePipeline<BinanceDialectWsApi, BinanceRateLimiterRef, BinanceSpotUserDataDispatcher<THandler>, TTelemetry> pipeline)
    {
        _pipeline = pipeline;
        _ = RunResubscribeLoopAsync(_appToken);
    }

    /// <inheritdoc />
    public override async ValueTask<WsStreamSubscribeStatus> SubscribeAsync(CancellationToken cancellationToken)
    {
        Pipeline.ThrowIfFatal();

        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_state.IsWanted)
            {
                return WsStreamSubscribeStatus.AlreadySubscribed;
            }

            // From here a new connection is subscribed by the hook too, which may confirm it first.
            lock (_state.HandlerSync)
            {
                _state.SetWanted();
            }
        }
        finally
        {
            _controlLock.Release();
        }

        try
        {
            return await SubscribeUntilAnsweredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RollBackAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public override async ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken)
    {
        Pipeline.ThrowIfFatal();

        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_state.IsWanted)
            {
                return false;
            }

            // Before the request: the eventStreamTerminated it brings must not read as unexpected.
            lock (_state.HandlerSync)
            {
                _state.ClearWanted();
            }

            _state.ClearResubscribe();

            // A connection that is not open holds no subscription of ours that could still deliver.
            if (!IsOpen(in _prepared))
            {
                _state.SubscribedEpoch = 0;
                return true;
            }

            WsApiRequestResult<SubscriptionModel> result =
                await _subscription.UnsubscribeAsync(Pipeline, _prepared, cancellationToken: cancellationToken).ConfigureAwait(false);

            _state.SubscribedEpoch = 0;
            return result.Outcome == RequestOutcome.Success || result.Errors.ExchangeCode == SubscriptionInactiveCode;
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <summary>Cold path. Subscribes the new connection while the user is subscribed. False drops it.</summary>
    public async ValueTask<bool> OnConnectedAsync(WsApiConnectionTicket ticket, CancellationToken cancellationToken)
    {
        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_state.IsWanted)
            {
                // Not subscribed: on a rotation the old connection stays current, after a drop the reconnect tries again.
                if (await SubscribeOnAsync(ticket, cancellationToken).ConfigureAwait(false) != WsStreamSubscribeStatus.Subscribed)
                {
                    return false;
                }

                CloseGap();
            }

            _prepared = ticket;
            return true;
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <summary>Subscribes until the exchange confirms or refuses. Waits for the connection before every try, as any WebSocket API call does.</summary>
    private async ValueTask<WsStreamSubscribeStatus> SubscribeUntilAnsweredAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (0 < attempt)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(GetBackoffDelayMillis(attempt)), cancellationToken).ConfigureAwait(false);
            }

            // Outside the control lock: the hook takes it while the connection is being prepared.
            await Pipeline.WaitConnectedAsync(RequestPriority.Normal, 0, cancellationToken).ConfigureAwait(false);
            await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // The hook subscribed a new connection meanwhile.
                if (_state.SubscribedEpoch != 0)
                {
                    return WsStreamSubscribeStatus.Subscribed;
                }

                // The connection dropped right after the wait: the next try waits for another one.
                if (!IsOpen(in _prepared))
                {
                    continue;
                }

                WsStreamSubscribeStatus status = await SubscribeOnAsync(_prepared, cancellationToken).ConfigureAwait(false);

                if (status == WsStreamSubscribeStatus.Rejected)
                {
                    lock (_state.HandlerSync)
                    {
                        _state.ClearWanted();
                    }
                }

                if (status != WsStreamSubscribeStatus.Unconfirmed)
                {
                    return status;
                }
            }
            finally
            {
                _controlLock.Release();
            }
        }
    }

    /// <summary>Cold path. Undoes a canceled subscribe: events the exchange may still send never reach the handler.</summary>
    private async ValueTask RollBackAsync()
    {
        try
        {
            // The app token, not the canceled one: the hook holds the lock for one request at most. On app shutdown nothing needs undoing.
            await _controlLock.WaitAsync(_appToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            lock (_state.HandlerSync)
            {
                _state.ClearWanted();
            }

            _state.ClearResubscribe();
            _state.SubscribedEpoch = 0;
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <summary>Subscribes again after an unexpected end of the subscription. Runs for the session lifetime.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A handler exception in a gap event is a bug and turns the session fatal.")]
    private async Task RunResubscribeLoopAsync(CancellationToken cancellationToken)
    {
        int failures = 0;

        try
        {
            while (true)
            {
                TimeSpan wait = failures == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(GetBackoffDelayMillis(failures));
                await _state.WaitAsync(wait, cancellationToken).ConfigureAwait(false);

                await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    failures = await TryResubscribeLockedAsync(cancellationToken).ConfigureAwait(false) ? 0 : failures + 1;
                }
                finally
                {
                    _controlLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
        catch (WsApiFatalException)
        {
            // The transport has already stopped and told the handler.
        }
        catch (Exception exception)
        {
            // Handlers must never throw: this is a bug, and retrying cannot fix it.
            WsDisconnectInfo fault = new() { Reason = WsDisconnectReason.DispatchFault, Fault = exception };
            Pipeline.EnterFatal(in fault);
        }
    }

    /// <summary>Subscribes the current connection again when asked to. False when it should be tried again later. Called under the control lock.</summary>
    private async ValueTask<bool> TryResubscribeLockedAsync(CancellationToken cancellationToken)
    {
        if (!_state.IsWanted || !_state.NeedsResubscribe)
        {
            return true;
        }

        // No open connection: the hook subscribes the next one.
        if (!IsOpen(in _prepared))
        {
            return true;
        }

        if (await SubscribeOnAsync(_prepared, cancellationToken).ConfigureAwait(false) != WsStreamSubscribeStatus.Subscribed)
        {
            return false;
        }

        CloseGap();
        return true;
    }

    /// <summary>Subscribes the account on the ticket's connection. Called under the control lock.</summary>
    private async ValueTask<WsStreamSubscribeStatus> SubscribeOnAsync(WsApiConnectionTicket ticket, CancellationToken cancellationToken)
    {
        WsApiRequestResult<SubscriptionModel> result =
            await _subscription.SubscribeAsync(Pipeline, ticket, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Outcome == RequestOutcome.Success || result.Errors.ExchangeCode == SubscriptionActiveCode)
        {
            _state.SubscribedEpoch = ticket.Connection.Epoch;
            _state.ClearResubscribe();
            return WsStreamSubscribeStatus.Subscribed;
        }

        // A definite refusal; anything else may still go through, e.g. after a lag or on a new connection.
        return result.Errors.Reason == WsApiRequestErrorReason.ExchangeApiRequestError
            ? WsStreamSubscribeStatus.Rejected
            : WsStreamSubscribeStatus.Unconfirmed;
    }

    private void CloseGap()
    {
        lock (_state.HandlerSync)
        {
            if (_state.TryCloseGap(long.MaxValue, out WsStreamGap gap))
            {
                _handler.OnReconnected(in gap);
            }
        }
    }

    private long GetBackoffDelayMillis(int attempt) =>
        WsReconnectPolicy.GetBackoffDelayMillis(attempt, _retryBaseDelayMillis, _retryMaxDelayMillis)
        + WsReconnectPolicy.GetJitterMillis(_minJitterMillis, _maxJitterMillis);

    private static bool IsOpen(in WsApiConnectionTicket ticket) =>
        ticket.Connection is not null && ticket.Connection.State == WsConnectionState.Open;
}
