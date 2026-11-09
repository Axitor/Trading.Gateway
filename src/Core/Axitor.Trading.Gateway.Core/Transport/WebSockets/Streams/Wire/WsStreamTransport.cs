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
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Subscriptions;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

/// <summary>Streams of one route: its connections, the subscriptions on them and the fatal state.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Connections open on demand. A shard connects when the first subscription lands on it,
/// and the next shard is used only once the previous ones are full.
/// </para>
/// <para>
/// DESIGN NOTE: A stream is registered before its subscribe request goes out, so data that follows the reply
/// already finds its handle. With no reply in time it stays registered; a reconnect subscribes it again.
/// </para>
/// </remarks>
internal sealed class WsStreamTransport<TDialect, TLimiter, TDispatcher, TTelemetry> : IDisposable
    where TDialect : struct, IExchangeDialectWsStream
    where TLimiter : struct, IRateLimiter
    where TDispatcher : struct, IWsStreamDispatcher
    where TTelemetry : struct, ITelemetryHandler
{
    // Room per stream name in a resubscribe request, plus its envelope.
    private const int NameReserveBytes = 64;
    private const int EnvelopeReserveBytes = 128;

    // Channel of streams routed by their name.
    private const int NameChannel = 0;

    private readonly WsConnectionFactory _factory;
    private readonly WsStreamRegistry _registry;
    private readonly WsStreamControlTable _control;
    private readonly WsStreamShard<TDialect, TLimiter, TDispatcher, TTelemetry>[] _shards;
    private readonly TDispatcher _dispatcher;
    private readonly BanGate _banGate;
    private readonly int _reconnectFallbackDelayMillis;
    private readonly long _responseTimeoutTicks;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _stopSource;
    private CancellationToken _appToken;
    private WsStreamFatalException? _fatal;
    private int _lastEpoch;

    /// <summary>Creates a transport for one route. Nothing connects until a subscription after Start. A request header, when given, goes with every handshake.</summary>
    public WsStreamTransport(
        WsStreamTransportOptions options,
        Uri uri,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter,
        TDispatcher dispatcher,
        TTelemetry telemetry,
        BanGate banGate,
        NetworkGate networkGate,
        NetworkRepairWorker networkRepairWorker,
        bool isWatchdog = false,
        string? requestHeaderName = null,
        string? requestHeaderValue = null)
    {
        _dispatcher = dispatcher;
        _banGate = banGate;
        _reconnectFallbackDelayMillis = options.Resiliency.ReconnectFallbackDelayMillis;
        _responseTimeoutTicks = SysTime.TicksFromMillis((long)options.Network.ResponseTimeout.TotalMilliseconds);

        int maxStreamsPerConnection = options.Network.MaxStreamsPerConnection;

        // A resubscribe sends every stream of a connection in one request.
        int sendBufferBytes = Math.Max(options.Buffer.SendBufferBytes, maxStreamsPerConnection * NameReserveBytes + EnvelopeReserveBytes);

        _factory = new WsConnectionFactory(
            options.Network.ConnectTimeout,
            options.Network.KeepAliveInterval,
            options.Network.KeepAliveTimeout,
            sendBufferBytes,
            options.Buffer.ReceiveBufferBytes,
            options.Network.MaxMessageBytes,
            requestHeaderName,
            requestHeaderValue);

        _registry = new WsStreamRegistry(options.Network.MaxConnections, maxStreamsPerConnection);
        _control = new WsStreamControlTable();
        _shards = new WsStreamShard<TDialect, TLimiter, TDispatcher, TTelemetry>[options.Network.MaxConnections];

        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new(this, i, options, uri, messagesPerSecond, connectLimitBucket, connectWeight,
                _factory, _registry, _control, limiter, dispatcher, telemetry, banGate, networkGate, networkRepairWorker, isWatchdog);
        }
    }

    /// <summary>Most subscriptions the route can hold. Handles are below this value.</summary>
    public int Capacity => _registry.Capacity;

    /// <summary>Whether the transport has stopped for good.</summary>
    public bool IsFatal => Volatile.Read(ref _fatal) is not null;

    /// <summary>Completes on app shutdown or Dispose; faults with WsStreamFatalException once the transport is fatal.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Arms the transport. Connections open on the first subscription. Call once.</summary>
    public void Start(CancellationToken appCancellationToken)
    {
        _appToken = appCancellationToken;
        CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(appCancellationToken);

        // A fatal faults the completion first, so this only completes a clean stop.
        _ = stopSource.Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), _completion);
        Volatile.Write(ref _stopSource, stopSource);
    }

    /// <summary>Cold path. Registers a stream routed by its name and subscribes it. Throws WsStreamFatalException once the transport is fatal.</summary>
    public ValueTask<WsStreamSubscribeResult> SubscribeAsync(ReadOnlyMemory<byte> name, CancellationToken cancellationToken) =>
        SubscribeAsync(name, NameChannel, name, cancellationToken);

    /// <summary>
    /// Cold path. Registers a stream routed by the channel and key its dialect reads, and subscribes it by name on the first connection with room.
    /// Throws WsStreamFatalException once the transport is fatal.
    /// </summary>
    public async ValueTask<WsStreamSubscribeResult> SubscribeAsync(ReadOnlyMemory<byte> name, int channel, ReadOnlyMemory<byte> key, CancellationToken cancellationToken)
    {
        CancellationToken stopToken = GetStopToken();
        ThrowIfFatal();

        if (!_registry.TryAdd(name.Span, channel, key.Span, out int handle, out int shardIndex))
        {
            return new WsStreamSubscribeResult()
            {
                Handle = handle,
                Status = handle < 0 ? WsStreamSubscribeStatus.CapacityExceeded : WsStreamSubscribeStatus.AlreadySubscribed,
            };
        }

        WsStreamShard<TDialect, TLimiter, TDispatcher, TTelemetry> shard = _shards[shardIndex];

        // Before the request goes out: state left from an earlier stream on this handle must not touch the new one.
        shard.NotifySubscribing(handle);

        _registry.TryGet(handle, out byte[] stored, out _);
        shard.EnsureStarted(stopToken, _appToken);

        long deadlineTicks = SysTime.GetTimestamp() + _responseTimeoutTicks;
        WsStreamControlReply reply = await shard.SendControlAsync(stored, subscribe: true, deadlineTicks, cancellationToken).ConfigureAwait(false);

        if (!reply.Received)
        {
            // The transport may have gone fatal while waiting: the caller gets the fatal, not a missing reply.
            ThrowIfFatal();
            return new WsStreamSubscribeResult() { Handle = handle, Status = WsStreamSubscribeStatus.Unconfirmed };
        }

        if (reply.IsError)
        {
            ThrowIfControlFatal(reply.ExchangeCode);
            _registry.TryRemove(handle);
            return new WsStreamSubscribeResult() { Handle = -1, Status = WsStreamSubscribeStatus.Rejected };
        }

        return new WsStreamSubscribeResult() { Handle = handle, Status = WsStreamSubscribeStatus.Subscribed };
    }

    /// <summary>
    /// Cold path. Unsubscribes a stream, frees its handle whatever the reply and tells the dispatcher. True once the exchange confirmed.
    /// Throws WsStreamFatalException once the transport is fatal.
    /// </summary>
    public async ValueTask<bool> UnsubscribeAsync(int handle, CancellationToken cancellationToken)
    {
        ThrowIfFatal();

        if (!_registry.TryGet(handle, out byte[] name, out int shardIndex))
        {
            return false;
        }

        long deadlineTicks = SysTime.GetTimestamp() + _responseTimeoutTicks;
        WsStreamControlReply reply = await _shards[shardIndex].SendControlAsync(name, subscribe: false, deadlineTicks, cancellationToken).ConfigureAwait(false);

        // Data reached the handle until now. From here, anything the exchange still sends is dropped as unknown.
        if (_registry.TryRemove(handle))
        {
            _shards[shardIndex].NotifyUnsubscribed(handle);
        }

        if (reply.IsError)
        {
            ThrowIfControlFatal(reply.ExchangeCode);
        }
        else if (!reply.Received)
        {
            ThrowIfFatal();
        }

        return reply.Received && !reply.IsError;
    }

    /// <summary>Next connection number. Shared by every shard.</summary>
    public int NextEpoch() => Interlocked.Increment(ref _lastEpoch);

    /// <summary>Drops every connection of the route at once. Each shard reconnects as its policy decides.</summary>
    public void AbortAll(WsDisconnectReason reason)
    {
        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i].Abort(reason);
        }
    }

    /// <summary>Throws WsStreamFatalException once the transport is fatal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfFatal()
    {
        WsStreamFatalException? fatal = Volatile.Read(ref _fatal);

        if (fatal is not null)
        {
            ThrowFatal(fatal);
        }
    }

    /// <summary>Cold path. Stops the transport for good and tells every subscription; the first caller's details are kept.</summary>
    public void EnterFatal(in WsDisconnectInfo info, int exchangeCode = 0)
    {
        WsStreamFatalException fatal = new(in info, exchangeCode);

        if (Interlocked.CompareExchange(ref _fatal, fatal, null) is not null)
        {
            return;
        }

        // A long ban covers REST and the WebSocket API too.
        if (info.Reason == WsDisconnectReason.HandshakeBanLong)
        {
            _banGate.SetBanMillis(WsReconnectPolicy.GetBanMillis(info.RetryAfterSeconds, _reconnectFallbackDelayMillis));
        }

        WsDisconnectReason reason = info.Reason == WsDisconnectReason.None ? WsDisconnectReason.ExchangeFatal : info.Reason;

        _control.FailAll();

        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i].Abort(reason);
        }

        NotifyFatal(reason);

        _completion.TrySetException(fatal);

        // Async: this runs on a receive loop or a supervisor, which must not be resumed inline.
        _ = _stopSource?.CancelAsync();
    }

    /// <summary>Tells every registered subscription that its data stopped for good, shard by shard under each shard's dispatch lock.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A handler that throws while told about the fatal changes nothing: the transport is already fatal.")]
    private void NotifyFatal(WsDisconnectReason reason)
    {
        for (int i = 0; i < _shards.Length; i++)
        {
            try
            {
                _shards[i].NotifyFatal(reason);
            }
            catch (Exception)
            {
                // Handlers must never throw; here it no longer matters, the other shards are still told.
            }
        }
    }

    /// <summary>Turns the transport fatal when the exchange refused a control request with a fatal code, and throws.</summary>
    private void ThrowIfControlFatal(int exchangeCode)
    {
        if (TDialect.ClassifyControlError(exchangeCode) != RequestErrorAction.Fatal)
        {
            return;
        }

        WsDisconnectInfo info = new() { Reason = WsDisconnectReason.ExchangeFatal };
        EnterFatal(in info, exchangeCode);
        ThrowIfFatal();
    }

    private CancellationToken GetStopToken()
    {
        CancellationTokenSource? source = Volatile.Read(ref _stopSource);

        if (source is null)
        {
            ThrowNotStarted();
        }

        return source.Token;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotStarted() =>
        throw new InvalidOperationException("The streams of this route are not enabled in the connector options.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFatal(WsStreamFatalException fatal) =>
        throw new WsStreamFatalException(fatal.Disconnect, fatal.ExchangeCode);

    /// <summary>Stops the shards and releases the sockets.</summary>
    public void Dispose()
    {
        _stopSource?.Cancel();
        _completion.TrySetResult();
        _factory.Dispose();
        _stopSource?.Dispose();
    }
}
