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
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>One lane of the WebSocket API: its connection, a draining predecessor during rotation, and the requests in flight.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: A connection hook prepares every new connection before it becomes current, e.g. subscribes user data on it.
/// The receive loop starts first, so the hook gets its replies. After a drop a failed hook drops the new connection too and the reconnect policy goes on;
/// on a rotation the old connection stays current and the new one goes.
/// </para>
/// <para>
/// DESIGN NOTE: A drop of the current connection opens a gap for the event dispatcher; it closes once a new connection has passed the hook.
/// If the replaced connection drops while the hook prepares the new one, events between the two may be lost, so a short gap is reported too.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The wake semaphore holds no unmanaged state without its wait handle; disposing it would make a late Release throw.")]
internal sealed class WsApiLane<TDialect, TLimiter, TEventDispatcher, TTelemetry>
    where TDialect : struct, IExchangeDialectWsApi
    where TLimiter : struct, IRateLimiter
    where TEventDispatcher : struct, IWsApiEventDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    private readonly WsApiTransport<TDialect, TLimiter, TEventDispatcher, TTelemetry> _transport;
    private readonly WsConnectionFactory _factory;
    private readonly BanGate _banGate;
    private readonly NetworkGate _networkGate;
    private readonly NetworkRepairWorker _networkRepairWorker;
    private readonly IWsApiConnectionHook? _hook;
    private readonly TLimiter _limiter;
    private readonly TEventDispatcher _dispatcher;
    private readonly TTelemetry _telemetry;
    private readonly Uri _uri;
    private readonly RequestPriority _role;
    private readonly int _messagesPerSecond;
    private readonly int _connectLimitBucket;
    private readonly int _connectWeight;
    private readonly int _drainMillis;
    private readonly int _reconnectBaseDelayMillis;
    private readonly int _reconnectMaxDelayMillis;
    private readonly int _minJitterMillisOnReconnect;
    private readonly int _maxJitterMillisOnReconnect;
    private readonly int _maxJitterMillisOnNetworkRepaired;
    private readonly int _reconnectFallbackDelayMillis;
    private readonly long _maxAgeTicks;
    private readonly long _attemptResetTicks;
    private readonly TimeSpan _closeTimeout;

    // Taken around every event dispatcher call; shared by the lanes of the transport.
    private readonly Lock _dispatchSync;

    // Wakes the supervisor when the current connection ends or the server announces shutdown.
    private readonly SemaphoreSlim _wake = new(0);

    private WsConnection? _current;
    private int _serverShutdownRequested;
    private CancellationToken _appToken;

    // Consecutive failed attempts. Touched by the supervisor only.
    private int _attempt;

    // Repair generation of the connection being opened, and of the one serving now. Touched by the supervisor only.
    private int _pendingRepairGeneration;
    private int _servedRepairGeneration;

    // Gap from a drop of the current connection to a new one past the hook. Touched by the supervisor only.
    private bool _gapOpen;
    private long _gapStartMillis;
    private WsDisconnectReason _gapReason;

    /// <summary>Creates a lane. Nothing connects until Start.</summary>
    public WsApiLane(
        WsApiTransport<TDialect, TLimiter, TEventDispatcher, TTelemetry> transport,
        RequestPriority role,
        WsApiTransportOptions options,
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        WsConnectionFactory factory,
        TLimiter limiter,
        TEventDispatcher dispatcher,
        TTelemetry telemetry,
        BanGate banGate,
        NetworkGate networkGate,
        NetworkRepairWorker networkRepairWorker,
        IWsApiConnectionHook? hook,
        Lock dispatchSync)
    {
        _transport = transport;
        _role = role;
        _uri = uri;
        _messagesPerSecond = messagesPerSecond;
        _connectLimitBucket = connectLimitBucket;
        _connectWeight = connectWeight;
        _factory = factory;
        _limiter = limiter;
        _dispatcher = dispatcher;
        _telemetry = telemetry;
        _banGate = banGate;
        _networkGate = networkGate;
        _networkRepairWorker = networkRepairWorker;
        _hook = hook;
        _dispatchSync = dispatchSync;

        _reconnectBaseDelayMillis = options.Resiliency.ReconnectBaseDelayMillis;
        _reconnectMaxDelayMillis = options.Resiliency.ReconnectMaxDelayMillis;
        _minJitterMillisOnReconnect = options.Resiliency.MinJitterMillisOnReconnect;
        _maxJitterMillisOnReconnect = options.Resiliency.MaxJitterMillisOnReconnect;
        _maxJitterMillisOnNetworkRepaired = options.Resiliency.MaxJitterMillisOnNetworkRepaired;
        _reconnectFallbackDelayMillis = options.Resiliency.ReconnectFallbackDelayMillis;

        _maxAgeTicks = SysTime.TicksFromMillis((long)options.Network.MaxConnectionAge.TotalMilliseconds);
        _attemptResetTicks = SysTime.TicksFromMillis(options.Resiliency.ReconnectAttemptResetMillis);
        _drainMillis = (int)(options.Network.ResponseTimeout + options.Network.TimeoutSweepInterval).TotalMilliseconds;
        _closeTimeout = options.Network.CloseTimeout;

        Table = new WsApiPendingTable(options.Network.MaxInFlight, options.Buffer.PendingSlotBufferBytes);
    }

    /// <summary>Requests in flight on this lane, across the current and the draining connection.</summary>
    public WsApiPendingTable Table { get; }

    /// <summary>The connection new requests go to, or null before the first connect.</summary>
    public WsConnection? Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _current);
    }

    /// <summary>Starts the supervisor. The stop token ends the lane; the app token outlives it for network repair.</summary>
    public void Start(CancellationToken stopToken, CancellationToken appToken)
    {
        _appToken = appToken;
        _ = SuperviseAsync(stopToken);
        _ = WatchRepairsAsync(stopToken);
    }

    /// <summary>Tears down the current connection at once.</summary>
    public void Abort(WsDisconnectReason reason) => Volatile.Read(ref _current)?.Abort(reason);

    [SuppressMessage("Design", "CA1031", Justification = "A hook or handler exception is a bug and turns the transport fatal.")]
    private async Task SuperviseAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                WsConnection? connection = await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);

                if (connection is null)
                {
                    return;
                }

                WsConnection dropped = await AttachAsync(connection, cancellationToken).ConfigureAwait(false)
                    ? await ServeAsync(connection, cancellationToken).ConfigureAwait(false)
                    : connection;

                if (!await PrepareReconnectAsync(dropped, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown or the transport went fatal.
        }
        catch (WsApiFatalException)
        {
            // A hook request saw the transport go fatal; it has already stopped.
        }
        catch (Exception exception)
        {
            // Hooks and handlers must never throw: this is a bug, and retrying cannot fix it.
            WsDisconnectInfo fault = new() { Reason = WsDisconnectReason.DispatchFault, Fault = exception };
            _transport.EnterFatal(in fault);
        }
    }

    /// <summary>Connects until it succeeds. Null when the transport went fatal or the app is stopping.</summary>
    private async ValueTask<WsConnection?> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitForGatesAsync(cancellationToken).ConfigureAwait(false);

            int epoch = _transport.NextEpoch();
            _pendingRepairGeneration = _networkGate.RepairGeneration;
            WsConnectResult result = await _factory.ConnectAsync<TDialect>(_uri, epoch, _messagesPerSecond, cancellationToken).ConfigureAwait(false);

            if (result.Connection is not null)
            {
                return result.Connection;
            }

            if (!OnConnectFailed(in result, epoch))
            {
                return null;
            }

            // Ban and network faults are waited out by the gates on the next pass.
            if (!WsReconnectPolicy.IsBanFault(result.Reason) && !WsReconnectPolicy.IsNetworkFault(result.Reason))
            {
                await Task.Delay((int)GetBackoffDelayMillis(_attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Starts reading the new connection, runs the hook on it and makes it current. False drops it.</summary>
    private async ValueTask<bool> AttachAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        StartReceiving(connection, cancellationToken);

        if (!await RunHookAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            connection.Abort(WsDisconnectReason.ConnectionHookFailed);
            return false;
        }

        Publish(connection);
        _servedRepairGeneration = _pendingRepairGeneration;
        EndGap();
        return true;
    }

    /// <summary>Serves the connection through its rotations. Returns the current connection once it has dropped.</summary>
    private async ValueTask<WsConnection> ServeAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        long rotateAtTicks = connection.OpenedTicks + _maxAgeTicks;
        WsDisconnectReason rotationReason = WsDisconnectReason.Rotation;

        while (true)
        {
            long waitMillis = SysTime.MillisFromTicks(rotateAtTicks - SysTime.GetTimestamp());
            await _wake.WaitAsync((int)Math.Clamp(waitMillis, 0, int.MaxValue), cancellationToken).ConfigureAwait(false);

            WsConnection current = Volatile.Read(ref _current)!;

            if (current.State == WsConnectionState.Closed)
            {
                return current;
            }

            // The network was repaired since this connection opened: it may have died quietly during the outage.
            if (_servedRepairGeneration != _networkGate.RepairGeneration)
            {
                rotationReason = WsDisconnectReason.NetworkRepaired;
                rotateAtTicks = 0;
            }

            if (Interlocked.Exchange(ref _serverShutdownRequested, 0) == 1)
            {
                rotationReason = WsDisconnectReason.ServerShutdown;
                rotateAtTicks = 0;
            }

            // A wake left over from a connection that is no longer current.
            if (SysTime.GetTimestamp() < rotateAtTicks)
            {
                continue;
            }

            if (await TryRotateAsync(current, rotationReason, cancellationToken).ConfigureAwait(false))
            {
                _attempt = 0;
                rotationReason = WsDisconnectReason.Rotation;
                rotateAtTicks = Volatile.Read(ref _current)!.OpenedTicks + _maxAgeTicks;
            }
            else
            {
                rotateAtTicks = SysTime.GetTimestamp() + SysTime.TicksFromMillis(GetBackoffDelayMillis(Math.Max(1, _attempt)));
            }
        }
    }

    /// <summary>Make-before-break: one attempt to open a replacement and run the hook on it. False leaves the old connection serving.</summary>
    private async ValueTask<bool> TryRotateAsync(WsConnection old, WsDisconnectReason reason, CancellationToken cancellationToken)
    {
        // No waiting on the gates: the old connection keeps serving, the rotation is tried again later.
        if (_banGate.IsBanned(out _) || !_networkGate.IsHealthy() || !_limiter.TryReserve(_connectLimitBucket, _connectWeight, out _))
        {
            return false;
        }

        int epoch = _transport.NextEpoch();
        int repairGeneration = _networkGate.RepairGeneration;
        WsConnectResult result = await _factory.ConnectAsync<TDialect>(_uri, epoch, _messagesPerSecond, cancellationToken).ConfigureAwait(false);

        if (result.Connection is null)
        {
            OnConnectFailed(in result, epoch);
            return false;
        }

        WsConnection fresh = result.Connection;
        long hookStartMillis = SysTime.UtcMillis();
        StartReceiving(fresh, cancellationToken);

        if (!await RunHookAsync(fresh, cancellationToken).ConfigureAwait(false))
        {
            // The old connection stays current; the replacement goes.
            fresh.Abort(WsDisconnectReason.ConnectionHookFailed);
            return false;
        }

        Publish(fresh);
        _servedRepairGeneration = repairGeneration;
        ReportOverlapGap(old, hookStartMillis);
        old.BeginDrain();
        _ = DrainAsync(old, reason, cancellationToken);
        return true;
    }

    /// <summary>Prepares the connection with the hook. True when there is no hook.</summary>
    private ValueTask<bool> RunHookAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        if (_hook is null)
        {
            return new ValueTask<bool>(true);
        }

        WsApiConnectionTicket ticket = new() { Connection = connection, Table = Table };
        return _hook.OnConnectedAsync(ticket, cancellationToken);
    }

    /// <summary>Wakes the supervisor on every network repair, so it can replace a connection that may have died quietly.</summary>
    private async Task WatchRepairsAsync(CancellationToken cancellationToken)
    {
        try
        {
            int seen = _networkGate.RepairGeneration;

            while (true)
            {
                // The signal first, then the generation: a repair in between is seen by one of them.
                Task nextRepair = _networkGate.NextRepair;

                if (_networkGate.RepairGeneration == seen)
                {
                    await nextRepair.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                seen = _networkGate.RepairGeneration;
                _wake.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown or the transport went fatal.
        }
    }

    /// <summary>Closes a replaced connection once every request on it has its response or its timeout.</summary>
    private async Task DrainAsync(WsConnection old, WsDisconnectReason reason, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_drainMillis, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping: the receive loop tears the connection down.
            return;
        }

        if (old.State != WsConnectionState.Closed)
        {
            await old.CloseAsync(reason, _closeTimeout).ConfigureAwait(false);
        }
    }

    /// <summary>Decides how to come back after the current connection dropped. False stops the lane.</summary>
    private async ValueTask<bool> PrepareReconnectAsync(WsConnection dropped, CancellationToken cancellationToken)
    {
        WsDisconnectReason reason = dropped.DisconnectReason;

        // A fatal transport has already told the dispatcher; a handler fault is reported by the receive loop that caught it.
        if (reason == WsDisconnectReason.DispatchFault || _transport.IsFatal)
        {
            return false;
        }

        if (WsReconnectPolicy.Classify(reason) != RequestErrorAction.Retry)
        {
            return false;
        }

        BeginGap(reason);

        // A connection that lived long enough starts the back-off over; a flapping one keeps it growing.
        bool livedLongEnough = _attemptResetTicks <= SysTime.GetTimestamp() - dropped.OpenedTicks;
        _attempt = livedLongEnough ? 1 : _attempt + 1;

        if (WsReconnectPolicy.IsNetworkFault(reason))
        {
            MarkNetworkDown();
            return true;
        }

        await Task.Delay((int)GetBackoffDelayMillis(_attempt), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Tells the dispatcher that events stopped. Once per gap.</summary>
    private void BeginGap(WsDisconnectReason reason)
    {
        if (_gapOpen)
        {
            return;
        }

        _gapOpen = true;
        _gapReason = reason;
        _gapStartMillis = SysTime.UtcMillis();

        lock (_dispatchSync)
        {
            _dispatcher.OnDisconnected(reason, isFatal: false);
        }
    }

    /// <summary>Tells the dispatcher that events flow again on a connection past the hook.</summary>
    private void EndGap()
    {
        if (!_gapOpen)
        {
            return;
        }

        _gapOpen = false;

        WsStreamGap gap = new()
        {
            Reason = _gapReason,
            StartUtcMillis = _gapStartMillis,
            EndUtcMillis = SysTime.UtcMillis(),
        };

        lock (_dispatchSync)
        {
            _dispatcher.OnReconnected(in gap);
        }
    }

    /// <summary>The replaced connection dropped while the hook prepared the new one: events between the two may be lost.</summary>
    private void ReportOverlapGap(WsConnection old, long startMillis)
    {
        if (old.State != WsConnectionState.Closed || _gapOpen)
        {
            return;
        }

        WsStreamGap gap = new()
        {
            Reason = old.DisconnectReason,
            StartUtcMillis = startMillis,
            EndUtcMillis = SysTime.UtcMillis(),
        };

        lock (_dispatchSync)
        {
            _dispatcher.OnDisconnected(gap.Reason, isFatal: false);
            _dispatcher.OnReconnected(in gap);
        }
    }

    /// <summary>Waits out a ban, a network outage and the connect rate limit.</summary>
    private async ValueTask WaitForGatesAsync(CancellationToken cancellationToken)
    {
        while (_banGate.IsBanned(out long waitBanMillis))
        {
            await Task.Delay((int)(waitBanMillis + GetReconnectJitterMillis()), cancellationToken).ConfigureAwait(false);
        }

        if (!_networkGate.IsHealthy() && await _networkGate.WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            await Task.Delay(WsReconnectPolicy.GetJitterMillis(0, _maxJitterMillisOnNetworkRepaired), cancellationToken).ConfigureAwait(false);
        }

        while (!_limiter.TryReserve(_connectLimitBucket, _connectWeight, out long waitMillis))
        {
            await Task.Delay((int)(waitMillis + GetReconnectJitterMillis()), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Cold path. Applies a failed attempt to the gates and the transport. False when no retry should follow.</summary>
    private bool OnConnectFailed(in WsConnectResult result, int epoch)
    {
        _attempt++;

        WsDisconnectInfo failure = new()
        {
            Reason = result.Reason,
            StatusCode = result.StatusCode,
            RetryAfterSeconds = result.RetryAfterSeconds,
            HttpError = result.HttpError,
            WebSocketError = result.WebSocketError,
            SocketError = result.SocketError,
        };

        RequestErrorAction action = WsReconnectPolicy.Classify(result.Reason);

        if (action == RequestErrorAction.Fatal)
        {
            _transport.EnterFatal(in failure);
        }
        else if (WsReconnectPolicy.IsBanFault(result.Reason))
        {
            _banGate.SetBanMillis(WsReconnectPolicy.GetBanMillis(result.RetryAfterSeconds, _reconnectFallbackDelayMillis));
        }
        else if (WsReconnectPolicy.IsNetworkFault(result.Reason))
        {
            MarkNetworkDown();
        }

        WsApiConnectionInfo info = Describe(epoch);
        _telemetry.OnWsApiConnectFailed(in info, in failure, _attempt);

        return action == RequestErrorAction.Retry;
    }

    private void MarkNetworkDown()
    {
        // App token: the repair serves REST too and must outlive a fatal WebSocket transport.
        if (_networkGate.MarkDown())
        {
            _networkRepairWorker.StartRepairLoop(_appToken);
        }
    }

    /// <summary>Starts reading the connection, before it becomes current.</summary>
    private void StartReceiving(WsConnection connection, CancellationToken cancellationToken)
    {
        _ = ReceiveLoopAsync(connection, cancellationToken);

        WsApiConnectionInfo info = Describe(connection.Epoch);
        _telemetry.OnWsApiConnected(in info);
    }

    /// <summary>Makes the connection current: new requests go to it.</summary>
    private void Publish(WsConnection connection)
    {
        Volatile.Write(ref _current, connection);
        _transport.NotifyConnected();

        // Dropped before it became current: its receive loop did not wake the supervisor.
        if (connection.State == WsConnectionState.Closed)
        {
            _wake.Release();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Any handler exception is a bug and turns the transport fatal.")]
    private async Task ReceiveLoopAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        WsApiConnectionInfo info = Describe(connection.Epoch);
        Exception? fault = null;

        while (await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                HandleMessage(connection, in info);
            }
            catch (Exception exception)
            {
                // Handlers must never throw: this is a bug, and retrying cannot fix it.
                fault = exception;
                connection.Abort(WsDisconnectReason.DispatchFault);
                break;
            }
        }

        WsDisconnectInfo disconnect = new()
        {
            Reason = connection.DisconnectReason,
            WebSocketError = connection.WebSocketError,
            SocketError = connection.SocketError,
            Fault = fault,
        };

        Table.FailConnection(connection.Epoch, disconnect.Reason);

        if (fault is not null)
        {
            _transport.EnterFatal(in disconnect);
        }
        else if (ReferenceEquals(Volatile.Read(ref _current), connection))
        {
            _wake.Release();
        }

        // Last: user code must not hold back the steps above.
        _telemetry.OnWsApiDisconnected(in info, in disconnect);
    }

    /// <summary>Routes one message. Runs on the receive loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleMessage(WsConnection connection, in WsApiConnectionInfo info)
    {
        ReadOnlySpan<byte> message = connection.Message;

        switch (TDialect.ReadInbound(message, out long requestId))
        {
            case WsInboundKind.Response:
                if (!Table.TryComplete(requestId, message))
                {
                    _telemetry.OnWsApiResponseUnmatched(in info, requestId);
                }
                return;

            case WsInboundKind.Push:
                DispatchPush(connection, message, in info);
                return;

            default:
                _telemetry.OnWsApiMessageUnknown(in info);
                return;
        }
    }

    /// <summary>Hands an event to the dispatcher one call at a time across every connection of the transport.</summary>
    private void DispatchPush(WsConnection connection, ReadOnlySpan<byte> message, in WsApiConnectionInfo info)
    {
        WsDispatchResult result;
        WsInboundSignal signal;

        lock (_dispatchSync)
        {
            result = _dispatcher.Dispatch(message, connection.Epoch, out signal);
        }

        if (signal == WsInboundSignal.ServerShutdown && ReferenceEquals(Volatile.Read(ref _current), connection))
        {
            Volatile.Write(ref _serverShutdownRequested, 1);
            _wake.Release();
        }

        if (result != WsDispatchResult.Handled)
        {
            ReportDispatchResult(result, in info);
        }
    }

    /// <summary>Cold path. Reports an event that was not handled cleanly; a fatal one stops the transport. Runs outside the dispatch lock.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReportDispatchResult(WsDispatchResult result, in WsApiConnectionInfo info)
    {
        switch (result)
        {
            case WsDispatchResult.Unreadable:
                _telemetry.OnWsApiMessageUnknown(in info);
                return;

            case WsDispatchResult.NonStandard:
                _telemetry.OnWsApiMessageNonStandard(in info);
                return;

            case WsDispatchResult.Fatal:
                WsDisconnectInfo fatal = new() { Reason = WsDisconnectReason.MessageUnreadable };
                _transport.EnterFatal(in fatal);
                return;
        }
    }

    /// <summary>Back-off plus jitter for the given consecutive attempt.</summary>
    private long GetBackoffDelayMillis(int attempt) =>
        WsReconnectPolicy.GetBackoffDelayMillis(attempt, _reconnectBaseDelayMillis, _reconnectMaxDelayMillis) + GetReconnectJitterMillis();

    private int GetReconnectJitterMillis() =>
        WsReconnectPolicy.GetJitterMillis(_minJitterMillisOnReconnect, _maxJitterMillisOnReconnect);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private WsApiConnectionInfo Describe(int epoch) => new() { Uri = _uri, Lane = _role, Epoch = epoch };
}
