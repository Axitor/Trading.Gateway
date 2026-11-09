using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Correlation;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Subscriptions;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

/// <summary>One connection of a stream route: connects, subscribes its streams, rotates by age and reports gaps per subscription.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: One control exchange at a time per shard: subscribe, unsubscribe, resubscribe after a reconnect, rotation.
/// So a subscribe request never goes to a connection that a rotation is about to close.
/// </para>
/// <para>
/// DESIGN NOTE: Rotation opens the new connection first, makes it current and subscribes every stream on it.
/// Once the exchange confirms, the old one closes; until then both deliver data, and the connector drops what it already handled.
/// Without a confirmation the old connection stays current and the rotation is tried again later.
/// </para>
/// <para>
/// DESIGN NOTE: After every network repair the connection is rotated the same way: it may have died quietly during the outage.
/// A connection opened after the last repair is not rotated again.
/// </para>
/// <para>
/// DESIGN NOTE: Every data message goes to the dispatcher under a shard lock. While a rotation overlaps,
/// the old and the new connection deliver the same subscriptions, and the handler must see them one at a time and in order.
/// Outside a rotation only one receive loop takes the lock, so it is never contended and costs about two atomic operations,
/// far less than receiving and decrypting the message.
/// </para>
/// <para>
/// DESIGN NOTE: The lock is not limited to the rotation on purpose. With a plain flag, the old loop can read it just before it is set
/// and then call the handler while the new loop already runs. Closing that gap needs an atomic step on every message,
/// which is what the lock already costs. Waiting for the old loop to confirm instead would hang when its stream is quiet.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphores hold no unmanaged state without a wait handle; disposing them would make a late Release throw.")]
internal sealed class WsStreamShard<TDialect, TLimiter, TDispatcher, TTelemetry>
    where TDialect : struct, IExchangeDialectWsStream
    where TLimiter : struct, IRateLimiter
    where TDispatcher : struct, IWsStreamDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    private readonly WsStreamTransport<TDialect, TLimiter, TDispatcher, TTelemetry> _transport;
    private readonly WsStreamRegistry _registry;
    private readonly WsStreamControlTable _control;
    private readonly WsConnectionFactory _factory;
    private readonly BanGate _banGate;
    private readonly NetworkGate _networkGate;
    private readonly NetworkRepairWorker _networkRepairWorker;
    private readonly TLimiter _limiter;
    private readonly TDispatcher _dispatcher;
    private readonly TTelemetry _telemetry;
    private readonly Uri _uri;
    private readonly int _index;
    private readonly int _messagesPerSecond;
    private readonly int _connectLimitBucket;
    private readonly int _connectWeight;
    private readonly int _reconnectBaseDelayMillis;
    private readonly int _reconnectMaxDelayMillis;
    private readonly int _minJitterMillisOnReconnect;
    private readonly int _maxJitterMillisOnReconnect;
    private readonly int _maxJitterMillisOnNetworkRepaired;
    private readonly int _reconnectFallbackDelayMillis;
    private readonly long _maxAgeTicks;
    private readonly long _attemptResetTicks;
    private readonly long _responseTimeoutTicks;
    private readonly TimeSpan _closeTimeout;

    // The watchdog reports through its own telemetry events; the regular stream events stay silent for it.
    private readonly bool _isWatchdog;

    // Wakes the supervisor when the current connection ends, the server announces shutdown or the network is repaired.
    private readonly SemaphoreSlim _wake = new(0);

    // One control exchange at a time: subscribe, unsubscribe, resubscribe, rotation.
    private readonly SemaphoreSlim _controlLock = new(1, 1);

    // PERF: every dispatcher call takes it; contended only while a rotation overlaps two receive loops.
    private readonly Lock _dispatchSync = new();

    // Names of a control request. Used under the control lock only.
    private readonly int[] _controlHandles;
    private readonly byte[][] _controlNames;

    // Subscriptions cut off by the last drop. Touched by the supervisor only.
    private readonly int[] _gapHandles;
    private readonly byte[][] _gapNames;
    private int _gapCount;
    private long _gapStartMillis;
    private WsDisconnectReason _gapReason;

    private WsConnection? _current;
    private TaskCompletionSource _connectedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _serverShutdownRequested;
    private CancellationToken _appToken;

    // Consecutive failed attempts. Touched by the supervisor only.
    private int _attempt;

    // Repair generation of the connection being opened, and of the one serving now. Touched by the supervisor only.
    private int _pendingRepairGeneration;
    private int _servedRepairGeneration;

    /// <summary>Creates a shard. Nothing connects until EnsureStarted.</summary>
    public WsStreamShard(
        WsStreamTransport<TDialect, TLimiter, TDispatcher, TTelemetry> transport,
        int index,
        WsStreamTransportOptions options,
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        WsConnectionFactory factory,
        WsStreamRegistry registry,
        WsStreamControlTable control,
        TLimiter limiter,
        TDispatcher dispatcher,
        TTelemetry telemetry,
        BanGate banGate,
        NetworkGate networkGate,
        NetworkRepairWorker networkRepairWorker,
        bool isWatchdog)
    {
        _transport = transport;
        _index = index;
        _uri = uri;
        _messagesPerSecond = messagesPerSecond;
        _connectLimitBucket = connectLimitBucket;
        _connectWeight = connectWeight;
        _factory = factory;
        _registry = registry;
        _control = control;
        _limiter = limiter;
        _dispatcher = dispatcher;
        _telemetry = telemetry;
        _banGate = banGate;
        _networkGate = networkGate;
        _networkRepairWorker = networkRepairWorker;
        _isWatchdog = isWatchdog;

        _reconnectBaseDelayMillis = options.Resiliency.ReconnectBaseDelayMillis;
        _reconnectMaxDelayMillis = options.Resiliency.ReconnectMaxDelayMillis;
        _minJitterMillisOnReconnect = options.Resiliency.MinJitterMillisOnReconnect;
        _maxJitterMillisOnReconnect = options.Resiliency.MaxJitterMillisOnReconnect;
        _maxJitterMillisOnNetworkRepaired = options.Resiliency.MaxJitterMillisOnNetworkRepaired;
        _reconnectFallbackDelayMillis = options.Resiliency.ReconnectFallbackDelayMillis;

        _maxAgeTicks = SysTime.TicksFromMillis((long)options.Network.MaxConnectionAge.TotalMilliseconds);
        _attemptResetTicks = SysTime.TicksFromMillis(options.Resiliency.ReconnectAttemptResetMillis);
        _responseTimeoutTicks = SysTime.TicksFromMillis((long)options.Network.ResponseTimeout.TotalMilliseconds);
        _closeTimeout = options.Network.CloseTimeout;

        int maxStreams = options.Network.MaxStreamsPerConnection;
        _controlHandles = new int[maxStreams];
        _controlNames = new byte[maxStreams][];
        _gapHandles = new int[maxStreams];
        _gapNames = new byte[maxStreams][];
    }

    /// <summary>Starts the supervisor on the first call; later calls do nothing.</summary>
    public void EnsureStarted(CancellationToken stopToken, CancellationToken appToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _appToken = appToken;
        _ = SuperviseAsync(stopToken);
        _ = WatchRepairsAsync(stopToken);
    }

    /// <summary>Tears down the current connection at once.</summary>
    public void Abort(WsDisconnectReason reason) => Volatile.Read(ref _current)?.Abort(reason);

    /// <summary>Cold path. Lets the dispatcher reset a handle before its subscribe request goes out.</summary>
    public void NotifySubscribing(int subscription)
    {
        lock (_dispatchSync)
        {
            _dispatcher.OnSubscribing(subscription);
        }
    }

    /// <summary>Cold path. Tells the dispatcher a subscription was removed. Data found for it just before is dropped by the dispatch check.</summary>
    public void NotifyUnsubscribed(int subscription)
    {
        lock (_dispatchSync)
        {
            _dispatcher.OnUnsubscribed(subscription);
        }
    }

    /// <summary>Cold path. Tells every subscription of the shard that its data stopped for good.</summary>
    public void NotifyFatal(WsDisconnectReason reason)
    {
        // Own buffers: the gap buffers belong to the supervisor, which may still be running.
        int[] handles = new int[_controlHandles.Length];
        byte[][] names = new byte[_controlHandles.Length][];
        int count = _registry.CopyShard(_index, handles, names);

        // Under the dispatch lock, so a receive loop still inside the handler finishes first.
        lock (_dispatchSync)
        {
            for (int i = 0; i < count; i++)
            {
                _dispatcher.OnDisconnected(handles[i], reason, isFatal: true);
            }
        }
    }

    /// <summary>Cold path. Sends one subscribe or unsubscribe request and waits for its reply until the deadline. Not received when no connection opened in time.</summary>
    public async ValueTask<WsStreamControlReply> SendControlAsync(byte[] name, bool subscribe, long deadlineTicks, CancellationToken cancellationToken)
    {
        if (!await WaitConnectedAsync(deadlineTicks, cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        if (!await _controlLock.WaitAsync(RemainingMillis(deadlineTicks), cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        try
        {
            // A drop or a failed rotation may have landed while waiting for the lock.
            WsConnection? connection = Volatile.Read(ref _current);

            if (connection is null || connection.State != WsConnectionState.Open)
            {
                return default;
            }

            _controlNames[0] = name;
            return await SendControlLockedAsync(connection, 1, subscribe, deadlineTicks, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A handler exception in a gap event is a bug and turns the transport fatal.")]
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

                await AttachAsync(connection, cancellationToken).ConfigureAwait(false);
                EndGap();

                WsConnection dropped = await ServeAsync(connection, cancellationToken).ConfigureAwait(false);

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
        catch (Exception exception)
        {
            // Handlers must never throw: this is a bug, and retrying cannot fix it.
            WsDisconnectInfo fault = new() { Reason = WsDisconnectReason.DispatchFault, Fault = exception };
            _transport.EnterFatal(in fault);
        }
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

    /// <summary>Makes the connection current and subscribes every stream of the shard on it. True once the exchange confirmed.</summary>
    private async ValueTask<bool> AttachAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Publish(connection, cancellationToken);
            return await SubscribeAllLockedAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <summary>Serves the connection through its rotations. Returns the current connection once it has dropped.</summary>
    private async ValueTask<WsConnection> ServeAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        _servedRepairGeneration = _pendingRepairGeneration;

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

    /// <summary>Make-before-break: one attempt to replace the connection. False leaves the old one serving.</summary>
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
        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Publish(fresh, cancellationToken);

            if (!await SubscribeAllLockedAsync(fresh, cancellationToken).ConfigureAwait(false))
            {
                // No confirmation: the old connection stays current, the replacement goes.
                Volatile.Write(ref _current, old);
                fresh.Abort(WsDisconnectReason.Rotation);

                // The old one may have dropped meanwhile; its receive loop did not wake us, as it was not current then.
                if (old.State == WsConnectionState.Closed)
                {
                    _wake.Release();
                }

                return false;
            }
        }
        finally
        {
            _controlLock.Release();
        }

        _servedRepairGeneration = repairGeneration;
        _ = CloseReplacedAsync(old, reason);
        return true;
    }

    private async Task CloseReplacedAsync(WsConnection old, WsDisconnectReason reason)
    {
        if (old.State != WsConnectionState.Closed)
        {
            await old.CloseAsync(reason, _closeTimeout).ConfigureAwait(false);
        }
    }

    /// <summary>Decides how to come back after the current connection dropped. False stops the shard.</summary>
    private async ValueTask<bool> PrepareReconnectAsync(WsConnection dropped, CancellationToken cancellationToken)
    {
        WsDisconnectReason reason = dropped.DisconnectReason;

        // A fatal transport has already told every subscription.
        if (_transport.IsFatal || WsReconnectPolicy.Classify(reason) != RequestErrorAction.Retry)
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

    /// <summary>Tells every subscription of the shard that its data stopped.</summary>
    private void BeginGap(WsDisconnectReason reason)
    {
        _gapCount = _registry.CopyShard(_index, _gapHandles, _gapNames);
        _gapStartMillis = SysTime.UtcMillis();
        _gapReason = reason;

        lock (_dispatchSync)
        {
            for (int i = 0; i < _gapCount; i++)
            {
                _dispatcher.OnDisconnected(_gapHandles[i], reason, isFatal: false);
            }
        }
    }

    /// <summary>Tells the subscriptions cut off by the last drop that data flows again.</summary>
    private void EndGap()
    {
        if (_gapCount == 0)
        {
            return;
        }

        WsStreamGap gap = new()
        {
            Reason = _gapReason,
            StartUtcMillis = _gapStartMillis,
            EndUtcMillis = SysTime.UtcMillis(),
        };

        // Data of the new connection may already flow: the handler sees these calls in turn with it.
        lock (_dispatchSync)
        {
            for (int i = 0; i < _gapCount; i++)
            {
                // Skipped when removed meanwhile, or when its handle now belongs to another stream.
                if (_registry.TryGet(_gapHandles[i], out byte[] name, out _) && ReferenceEquals(name, _gapNames[i]))
                {
                    _dispatcher.OnReconnected(_gapHandles[i], in gap);
                }
            }
        }

        if (_isWatchdog)
        {
            _telemetry.OnWatchdogResumed(in gap);
        }

        _gapCount = 0;
    }

    /// <summary>Subscribes every stream of the shard on the connection. Called under the control lock. True once the exchange confirmed.</summary>
    private async ValueTask<bool> SubscribeAllLockedAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        int count = _registry.CopyShard(_index, _controlHandles, _controlNames);

        if (count == 0)
        {
            return true;
        }

        long deadlineTicks = SysTime.GetTimestamp() + _responseTimeoutTicks;
        WsStreamControlReply reply = await SendControlLockedAsync(connection, count, subscribe: true, deadlineTicks, cancellationToken).ConfigureAwait(false);

        if (reply.IsError && TDialect.ClassifyControlError(reply.ExchangeCode) == RequestErrorAction.Fatal)
        {
            WsDisconnectInfo info = new() { Reason = WsDisconnectReason.ExchangeFatal };
            _transport.EnterFatal(in info, reply.ExchangeCode);
        }

        return reply.Received && !reply.IsError;
    }

    /// <summary>Sends the names in the control buffer and waits for the reply until the deadline. Called under the control lock.</summary>
    private async ValueTask<WsStreamControlReply> SendControlLockedAsync(WsConnection connection, int count, bool subscribe, long deadlineTicks, CancellationToken cancellationToken)
    {
        long requestId = _control.Register(connection.Epoch, out Task<WsStreamControlReply> reply);
        WsSendStatus status;

        try
        {
            status = await connection.SendAsync(new WsStreamControlFrame<TDialect>(_controlNames, count, requestId, subscribe), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Shutdown or a frame too big for the buffer: nobody will wait for this id.
            _control.Forget(requestId);
            throw;
        }

        if (status != WsSendStatus.Sent)
        {
            _control.Forget(requestId);
            return default;
        }

        try
        {
            return await reply.WaitAsync(TimeSpan.FromMilliseconds(RemainingMillis(deadlineTicks)), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A late reply is then unmatched and goes to telemetry.
            _control.Forget(requestId);
            return default;
        }
    }

    /// <summary>Waits until the shard has an open connection. False when the deadline passed first.</summary>
    private async ValueTask<bool> WaitConnectedAsync(long deadlineTicks, CancellationToken cancellationToken)
    {
        while (true)
        {
            // Signal first, then the check: a connect landing in between is seen by the check.
            TaskCompletionSource source = Volatile.Read(ref _connectedSource);
            WsConnection? connection = Volatile.Read(ref _current);

            if (connection is not null && connection.State == WsConnectionState.Open)
            {
                return true;
            }

            int remainingMillis = RemainingMillis(deadlineTicks);

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

        if (_isWatchdog)
        {
            _telemetry.OnWatchdogConnectFailed(in failure, _attempt);
        }
        else
        {
            WsStreamConnectionInfo info = Describe(epoch);
            _telemetry.OnWsStreamConnectFailed(in info, in failure, _attempt);
        }

        return action == RequestErrorAction.Retry;
    }

    private void MarkNetworkDown()
    {
        // App token: the repair serves REST too and must outlive a fatal stream transport.
        if (_networkGate.MarkDown())
        {
            _networkRepairWorker.StartRepairLoop(_appToken);
        }
    }

    /// <summary>Makes the connection current and starts reading it.</summary>
    private void Publish(WsConnection connection, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _current, connection);
        _ = ReceiveLoopAsync(connection, cancellationToken);
        NotifyConnected();

        if (!_isWatchdog)
        {
            WsStreamConnectionInfo info = Describe(connection.Epoch);
            _telemetry.OnWsStreamConnected(in info);
        }
    }

    /// <summary>Wakes control requests waiting for a connection.</summary>
    private void NotifyConnected()
    {
        TaskCompletionSource fresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _connectedSource, fresh).TrySetResult();
    }

    [SuppressMessage("Design", "CA1031", Justification = "Any handler exception is a bug and turns the transport fatal.")]
    private async Task ReceiveLoopAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        WsStreamConnectionInfo info = Describe(connection.Epoch);
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

        _control.FailConnection(connection.Epoch);

        if (fault is not null)
        {
            _transport.EnterFatal(in disconnect);
        }
        else if (ReferenceEquals(Volatile.Read(ref _current), connection))
        {
            _wake.Release();
        }

        // Last: user code must not hold back the steps above.
        if (!_isWatchdog)
        {
            _telemetry.OnWsStreamDisconnected(in info, in disconnect);
        }
    }

    /// <summary>Routes one message. Runs on the receive loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleMessage(WsConnection connection, in WsStreamConnectionInfo info)
    {
        ReadOnlySpan<byte> message = connection.Message;

        switch (TDialect.ReadInbound(message, connection.IsBinary, out WsStreamInbound inbound))
        {
            case WsInboundKind.Push:
                if (inbound.KeyLength == 0)
                {
                    DispatchEvent(connection, message, inbound.Signal);
                    return;
                }

                int handle = _registry.Find(inbound.Channel, message.Slice(inbound.KeyStart, inbound.KeyLength), out byte[]? registered);

                if (handle < 0)
                {
                    if (!_isWatchdog)
                    {
                        _telemetry.OnWsStreamMessageUnknown(in info);
                    }

                    return;
                }

                WsDispatchResult result = DispatchLocked(handle, registered!, message.Slice(inbound.DataStart, inbound.DataLength));

                if (result != WsDispatchResult.Handled)
                {
                    ReportDispatchResult(result, in info);
                }

                return;

            case WsInboundKind.Response:
                if (!_control.TryComplete(inbound.RequestId, inbound.IsError, inbound.ExchangeCode) && !_isWatchdog)
                {
                    _telemetry.OnWsStreamReplyUnmatched(in info, inbound.RequestId);
                }
                return;

            default:
                if (!_isWatchdog)
                {
                    _telemetry.OnWsStreamMessageUnknown(in info);
                }
                return;
        }
    }

    /// <summary>Hands data to the dispatcher one call at a time across the receive loops of the shard.</summary>
    private WsDispatchResult DispatchLocked(int handle, byte[] registered, ReadOnlySpan<byte> data)
    {
        lock (_dispatchSync)
        {
            // Removed or given to another stream since the lookup: reported like data of a stream no longer subscribed.
            if (!_registry.IsCurrent(handle, registered))
            {
                return WsDispatchResult.Unreadable;
            }

            return _dispatcher.Dispatch(handle, data);
        }
    }

    /// <summary>Cold path. Reports a message that was not handled cleanly; a fatal one stops the transport. Runs outside the dispatch lock.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReportDispatchResult(WsDispatchResult result, in WsStreamConnectionInfo info)
    {
        switch (result)
        {
            case WsDispatchResult.Unreadable:
                if (!_isWatchdog)
                {
                    _telemetry.OnWsStreamMessageUnknown(in info);
                }
                return;

            case WsDispatchResult.NonStandard:
                if (!_isWatchdog)
                {
                    _telemetry.OnWsStreamMessageNonStandard(in info);
                }
                return;

            case WsDispatchResult.Fatal:
                // Outside the dispatch lock: the fatal path takes it on every shard to tell the subscriptions.
                WsDisconnectInfo fatal = new() { Reason = WsDisconnectReason.MessageUnreadable };
                _transport.EnterFatal(in fatal);
                return;
        }
    }

    /// <summary>Cold path. An exchange event outside any subscription. A shutdown notice, read by the dialect or the dispatcher, starts a rotation.</summary>
    private void DispatchEvent(WsConnection connection, ReadOnlySpan<byte> message, WsInboundSignal signal)
    {
        if (signal == WsInboundSignal.None)
        {
            signal = _dispatcher.DispatchEvent(message);
        }

        if (signal == WsInboundSignal.ServerShutdown && ReferenceEquals(Volatile.Read(ref _current), connection))
        {
            Volatile.Write(ref _serverShutdownRequested, 1);
            _wake.Release();
        }
    }

    /// <summary>Back-off plus jitter for the given consecutive attempt.</summary>
    private long GetBackoffDelayMillis(int attempt) =>
        WsReconnectPolicy.GetBackoffDelayMillis(attempt, _reconnectBaseDelayMillis, _reconnectMaxDelayMillis) + GetReconnectJitterMillis();

    private int GetReconnectJitterMillis() =>
        WsReconnectPolicy.GetJitterMillis(_minJitterMillisOnReconnect, _maxJitterMillisOnReconnect);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RemainingMillis(long deadlineTicks) =>
        (int)Math.Clamp(SysTime.MillisFromTicks(deadlineTicks - SysTime.GetTimestamp()), 0, int.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private WsStreamConnectionInfo Describe(int epoch) => new() { Uri = _uri, Shard = _index, Epoch = epoch };
}
