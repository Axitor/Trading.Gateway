using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>WebSocket API of one market: a Critical and a Normal lane, or a single lane, request ids, timeouts and the fatal state.</summary>
/// <remarks>
/// DESIGN NOTE: A single lane serves one session on its own connection, e.g. user data: Critical requests go to it too.
/// </remarks>
internal sealed class WsApiTransport<TDialect, TLimiter, TEventDispatcher, TTelemetry> : IDisposable
    where TDialect : struct, IExchangeDialectWsApi
    where TLimiter : struct, IRateLimiter
    where TEventDispatcher : struct, IWsApiEventDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    private readonly WsConnectionFactory _factory;
    private readonly WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry>? _critical;
    private readonly WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry> _normal;
    private readonly WsApiTimeoutSweeper _sweeper;
    private readonly BanGate _banGate;
    private readonly TEventDispatcher _dispatcher;
    private readonly int _reconnectFallbackDelayMillis;
    private readonly int _retryFallbackDelayMillis;
    private readonly long _responseTimeoutTicks;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Every event dispatcher call takes it, from every lane: a rotation overlap delivers events on two connections.
    private readonly Lock _dispatchSync = new();

    private CancellationTokenSource? _stopSource;
    private WsApiFatalException? _fatal;
    private TaskCompletionSource _connectedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lastEpoch;

    /// <summary>Creates a transport. Nothing connects until Start.</summary>
    public WsApiTransport(
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
        _dispatcher = dispatcher;
        _reconnectFallbackDelayMillis = options.Resiliency.ReconnectFallbackDelayMillis;
        _retryFallbackDelayMillis = options.Resiliency.RetryFallbackDelayMillis;
        _responseTimeoutTicks = SysTime.TicksFromMillis((long)options.Network.ResponseTimeout.TotalMilliseconds);

        _factory = new WsConnectionFactory(
            options.Network.ConnectTimeout,
            options.Network.KeepAliveInterval,
            options.Network.KeepAliveTimeout,
            options.Buffer.SendBufferBytes,
            options.Buffer.ReceiveBufferBytes,
            options.Network.MaxMessageBytes);

        _critical = singleLane
            ? null
            : new(this, RequestPriority.Critical, options, uri, messagesPerSecond, connectLimitBucket, connectWeight,
                _factory, limiter, dispatcher, telemetry, banGate, networkGate, networkRepairWorker, hook, _dispatchSync);

        _normal = new(this, RequestPriority.Normal, options, uri, messagesPerSecond, connectLimitBucket, connectWeight,
            _factory, limiter, dispatcher, telemetry, banGate, networkGate, networkRepairWorker, hook, _dispatchSync);

        _sweeper = new WsApiTimeoutSweeper(_critical?.Table, _normal.Table, options.Network.TimeoutSweepInterval);
    }

    /// <summary>Completes on app shutdown or Dispose; faults with WsApiFatalException once the transport is fatal.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Whether the transport has stopped for good.</summary>
    public bool IsFatal => Volatile.Read(ref _fatal) is not null;

    /// <summary>Starts the lanes and the timeout sweeper. Connections open in the background. Call once.</summary>
    public void Start(CancellationToken appCancellationToken)
    {
        CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(appCancellationToken);
        CancellationToken stopToken = stopSource.Token;

        // A fatal faults the completion first, so this only completes a clean stop.
        _ = stopToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), _completion);
        Volatile.Write(ref _stopSource, stopSource);

        _sweeper.Start(stopToken);
        _critical?.Start(stopToken, appCancellationToken);
        _normal.Start(stopToken, appCancellationToken);
    }

    /// <summary>Sends one request on the lane of its priority and waits for its outcome. Throws WsApiFatalException once the transport is fatal.</summary>
    public ValueTask<WsApiTransportResult> SendAsync<TFrame>(RequestPriority priority, TFrame frame, CancellationToken cancellationToken)
        where TFrame : struct, IWsApiFrameWriter
    {
        ThrowIfFatal();

        WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry> lane = SelectLane(priority, out WsConnection? connection);

        if (connection is null)
        {
            return new ValueTask<WsApiTransportResult>(Rejected(WsApiPendingOutcome.NoConnection));
        }

        return SendOnConnectionAsync(lane.Table, connection, frame, cancellationToken);
    }

    /// <summary>Sends one request strictly on the ticket's connection, current or not. Throws WsApiFatalException once the transport is fatal.</summary>
    public ValueTask<WsApiTransportResult> SendOnAsync<TFrame>(WsApiConnectionTicket ticket, TFrame frame, CancellationToken cancellationToken)
        where TFrame : struct, IWsApiFrameWriter
    {
        ThrowIfFatal();
        return SendOnConnectionAsync(ticket.Table, ticket.Connection, frame, cancellationToken);
    }

    /// <summary>The open connection the priority would use now. False when none is open.</summary>
    public bool TryGetConnection(RequestPriority priority, out WsApiConnectionTicket ticket)
    {
        WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry> lane = SelectLane(priority, out WsConnection? connection);

        if (connection is null)
        {
            ticket = default;
            return false;
        }

        ticket = new WsApiConnectionTicket() { Connection = connection, Table = lane.Table };
        return true;
    }

    /// <summary>Next connection number. Shared by every lane.</summary>
    public int NextEpoch() => Interlocked.Increment(ref _lastEpoch);

    /// <summary>Whether a lane the priority can use is open.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasConnection(RequestPriority priority)
    {
        SelectLane(priority, out WsConnection? connection);
        return connection is not null;
    }

    /// <summary>Cold path. Waits until a lane the priority can use opens. False when the deadline passed first; a zero deadline waits for good. Throws once the transport is fatal or if it was never started.</summary>
    public async ValueTask<bool> WaitConnectedAsync(RequestPriority priority, long deadlineTicks, CancellationToken cancellationToken)
    {
        // A market left out of the options never connects: fail fast instead of waiting for good.
        if (Volatile.Read(ref _stopSource) is null)
        {
            ThrowNotStarted();
        }

        while (true)
        {
            // Signal first, then the check: a connect landing in between is seen by the check.
            TaskCompletionSource source = Volatile.Read(ref _connectedSource);

            ThrowIfFatal();

            if (HasConnection(priority))
            {
                return true;
            }

            if (deadlineTicks == 0)
            {
                await source.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            long remainingMillis = SysTime.MillisFromTicks(deadlineTicks - SysTime.GetTimestamp());

            if (remainingMillis <= 0)
            {
                return false;
            }

            try
            {
                await source.Task.WaitAsync(TimeSpan.FromMilliseconds(remainingMillis), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotStarted() =>
    throw new InvalidOperationException("The WebSocket API of this market is not enabled in the connector options.");

    /// <summary>Wakes requests waiting for a connection. Called once a new connection is current.</summary>
    public void NotifyConnected()
    {
        TaskCompletionSource fresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _connectedSource, fresh).TrySetResult();
    }

    /// <summary>Throws WsApiFatalException once the transport is fatal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfFatal()
    {
        WsApiFatalException? fatal = Volatile.Read(ref _fatal);

        if (fatal is not null)
        {
            ThrowFatal(fatal);
        }
    }

    /// <summary>Cold path. Stops the transport for good on a connection that cannot be kept; the first caller's errors are kept.</summary>
    public void EnterFatal(in WsDisconnectInfo info)
    {
        WsApiRequestResultError errors = new()
        {
            Reason = WsApiRequestErrorReason.ConnectionFatal,
            StatusCode = info.StatusCode,
            Disconnect = info,
        };

        EnterFatal(in errors, default);
    }

    /// <summary>Cold path. Stops the transport for good and tells the event dispatcher; the first caller's errors are kept.</summary>
    public void EnterFatal(in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency)
    {
        WsApiFatalException fatal = new(in errors, in resiliency);

        if (Interlocked.CompareExchange(ref _fatal, fatal, null) is not null)
        {
            return;
        }

        // As with a stream fatal: a long ban covers REST too.
        _banGate.SetBanMillis(GetFatalBanMillis(in errors));

        WsDisconnectReason reason = errors.Disconnect.Reason == WsDisconnectReason.None ? WsDisconnectReason.ExchangeFatal : errors.Disconnect.Reason;

        _critical?.Table.FailAll(reason);
        _normal.Table.FailAll(reason);
        _critical?.Abort(reason);
        _normal.Abort(reason);

        NotifyFatal(reason);
        _completion.TrySetException(fatal);
        NotifyConnected();

        // Async: this runs on a receive loop or a supervisor, which must not be resumed inline.
        _ = _stopSource?.CancelAsync();
    }

    /// <summary>Sends on the given connection and table, and waits for the outcome.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<WsApiTransportResult> SendOnConnectionAsync<TFrame>(WsApiPendingTable table, WsConnection connection, TFrame frame, CancellationToken cancellationToken)
        where TFrame : struct, IWsApiFrameWriter
    {
        if (!table.TryAcquire(connection.Epoch, out WsApiPendingSlot? slot, out long requestId))
        {
            return Rejected(WsApiPendingOutcome.InFlightLimit);
        }

        ValueTask<WsApiPendingCompletion> wait = slot.WaitAsync();
        WsSendStatus status;

        try
        {
            status = await connection.SendAsync(new WsApiFrame<TFrame>(frame, requestId, slot), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Shutdown or a frame too big for the buffer: nobody will wait for this slot, so hand it back now.
            slot.TryFail(requestId, WsApiPendingOutcome.NotSent, WsDisconnectReason.None);
            await wait.ConfigureAwait(false);
            slot.Release();
            throw;
        }

        if (status == WsSendStatus.Sent)
        {
            slot.ArmDeadline(SysTime.GetTimestamp() + _responseTimeoutTicks);
        }
        else
        {
            slot.TryFail(requestId, status == WsSendStatus.NotSent ? WsApiPendingOutcome.NotSent : WsApiPendingOutcome.ConnectionLost, connection.DisconnectReason);
        }

        WsApiPendingCompletion completion = await wait.ConfigureAwait(false);

        // NotSent: the connection died while the request waited for the message budget. No order check needed.
        if (status == WsSendStatus.NotSent)
        {
            completion.Outcome = WsApiPendingOutcome.NotSent;
        }

        return new WsApiTransportResult() { Slot = slot, Completion = completion, RequestId = requestId };
    }

    /// <summary>Tells the event dispatcher that events stopped for good.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A handler that throws while told about the fatal changes nothing: the transport is already fatal.")]
    private void NotifyFatal(WsDisconnectReason reason)
    {
        try
        {
            lock (_dispatchSync)
            {
                _dispatcher.OnDisconnected(reason, isFatal: true);
            }
        }
        catch (Exception)
        {
            // Handlers must never throw; here it no longer matters.
        }
    }

    /// <summary>Ban a fatal leaves on the shared gate, or zero for none.</summary>
    private long GetFatalBanMillis(in WsApiRequestResultError errors)
    {
        if (errors.Disconnect.Reason == WsDisconnectReason.HandshakeBanLong)
        {
            return WsReconnectPolicy.GetBanMillis(errors.Disconnect.RetryAfterSeconds, _reconnectFallbackDelayMillis);
        }

        if (errors.Reason == WsApiRequestErrorReason.ExchangeApiBanLong)
        {
            return 0 < errors.RetryAfterMillis ? errors.RetryAfterMillis : _retryFallbackDelayMillis;
        }

        return 0;
    }

    /// <summary>Critical falls back to Normal while its own lane reconnects; Normal never takes the Critical lane. A single lane serves both.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry> SelectLane(RequestPriority priority, out WsConnection? connection)
    {
        if (priority == RequestPriority.Critical && _critical is not null)
        {
            connection = _critical.Current;

            if (connection is not null && connection.State == WsConnectionState.Open)
            {
                return _critical;
            }
        }

        connection = _normal.Current;

        if (connection is not null && connection.State != WsConnectionState.Open)
        {
            connection = null;
        }

        return _normal;
    }

    private static WsApiTransportResult Rejected(WsApiPendingOutcome outcome) =>
        new() { Completion = new WsApiPendingCompletion() { Outcome = outcome } };

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFatal(WsApiFatalException fatal) =>
        throw new WsApiFatalException(fatal.Errors, fatal.Resiliency);

    /// <summary>Stops the lanes and releases the sockets.</summary>
    public void Dispose()
    {
        _stopSource?.Cancel();
        _completion.TrySetResult();
        _sweeper.Dispose();
        _factory.Dispose();
        _stopSource?.Dispose();
    }
}
