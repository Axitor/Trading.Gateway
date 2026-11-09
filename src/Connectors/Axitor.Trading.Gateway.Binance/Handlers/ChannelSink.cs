using System.Threading.Channels;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>Queues the messages and connection events of every subscription for one consumer. Pass Handler to a stream endpoint's CreateSession.</summary>
/// <remarks>
/// <para>For streams where every message counts, e.g. trades. Read from one consumer with WaitToReadAsync and TryRead.</para>
/// <para>
/// A full queue never holds up the receive loop: that would stall every stream on the connection and end in a dropped connection.
/// Messages that do not fit are dropped instead, and an Overflow item comes before the next message of that subscription,
/// with the period and the number of messages lost. Treat it like Reconnected: rebuild any state made from the lost messages, e.g. volume.
/// DroppedCount shows how often this happens; raise the capacity or speed up the consumer if it grows.
/// </para>
/// <para>
/// Connection events are never dropped. If the consumer stops reading for so long that even they no longer fit, the queue ends with an error.
/// The queue also ends with an error when the session stops for good; the full cause is in the session Completion.
/// </para>
/// </remarks>
public sealed class ChannelSink<TModel> where TModel : struct
{
    // Room kept for connection events: every subscription breaking twice over.
    private const int EventsPerSubscription = 4;

    private readonly Channel<StreamItem<TModel>> _channel;
    private readonly int _capacity;
    private readonly long[] _droppedSince;
    private readonly long[] _overflowStartMillis;
    private int _queuedMessages;
    private long _droppedCount;

    /// <summary>Creates a sink holding up to capacity messages. Pass BinanceClient.StreamSessionCapacity as subscriptionCapacity.</summary>
    public ChannelSink(int capacity, int subscriptionCapacity)
    {
        _capacity = capacity;
        _droppedSince = new long[subscriptionCapacity];
        _overflowStartMillis = new long[subscriptionCapacity];

        _channel = Channel.CreateBounded<StreamItem<TModel>>(new BoundedChannelOptions(capacity + subscriptionCapacity * EventsPerSubscription)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        Handler = new ChannelSinkHandler<TModel>(this);
    }

    /// <summary>The handler to pass to CreateSession.</summary>
    public ChannelSinkHandler<TModel> Handler { get; }

    /// <summary>Messages dropped because the queue was full, across every subscription.</summary>
    public long DroppedCount => Volatile.Read(ref _droppedCount);

    /// <summary>Completes once the queue has ended and is read out; faults when it ended with an error.</summary>
    public Task Completion => _channel.Reader.Completion;

    /// <summary>Waits until an item can be read. False once the queue has ended and is read out; throws the error it ended with.</summary>
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <summary>Takes the next item. False when none is queued.</summary>
    public bool TryRead(out StreamItem<TModel> item)
    {
        if (!_channel.Reader.TryRead(out item))
        {
            return false;
        }

        if (item.Kind == StreamItemKind.Data)
        {
            Interlocked.Decrement(ref _queuedMessages);
        }

        return true;
    }

    internal void WriteData(int subscription, in TModel model)
    {
        if (_capacity < Interlocked.Increment(ref _queuedMessages))
        {
            Interlocked.Decrement(ref _queuedMessages);
            Drop(subscription);
            return;
        }

        FlushOverflow(subscription);
        WriteItem(new StreamItem<TModel>() { Kind = StreamItemKind.Data, Subscription = subscription, Model = model });
    }

    internal void WriteEvent(int subscription, StreamItemKind kind, WsDisconnectReason reason, in WsStreamGap gap)
    {
        FlushOverflow(subscription);
        WriteItem(new StreamItem<TModel>() { Kind = kind, Subscription = subscription, Reason = reason, Gap = gap });
    }

    internal void Fail(WsDisconnectReason reason)
    {
        WsDisconnectInfo info = new() { Reason = reason };
        _channel.Writer.TryComplete(new WsStreamFatalException(in info));
    }

    /// <summary>Counts a dropped message; the first one opens the overflow period of the subscription.</summary>
    private void Drop(int subscription)
    {
        if (_droppedSince[subscription] == 0)
        {
            _overflowStartMillis[subscription] = SysTime.UtcMillis();
        }

        _droppedSince[subscription]++;
        Interlocked.Increment(ref _droppedCount);
    }

    /// <summary>Queues the Overflow item of the subscription before anything else of it, if messages were dropped.</summary>
    private void FlushOverflow(int subscription)
    {
        long dropped = _droppedSince[subscription];

        if (dropped == 0)
        {
            return;
        }

        _droppedSince[subscription] = 0;

        WriteItem(new StreamItem<TModel>()
        {
            Kind = StreamItemKind.Overflow,
            Subscription = subscription,
            DroppedCount = dropped,
            Gap = new WsStreamGap() { StartUtcMillis = _overflowStartMillis[subscription], EndUtcMillis = SysTime.UtcMillis() },
        });
    }

    /// <summary>Room for events is kept apart from messages, so this fails only once the consumer stopped reading or the queue has ended.</summary>
    private void WriteItem(in StreamItem<TModel> item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            _channel.Writer.TryComplete(new InvalidOperationException("The consumer of the channel sink stopped reading."));
        }
    }
}

/// <summary>Stream handler of a ChannelSink.</summary>
public readonly struct ChannelSinkHandler<TModel> : IBinanceStreamHandler<TModel> where TModel : struct
{
    private readonly ChannelSink<TModel> _sink;

    internal ChannelSinkHandler(ChannelSink<TModel> sink) => _sink = sink;

    /// <inheritdoc />
    public void OnData(int subscription, in TModel model) => _sink.WriteData(subscription, in model);

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal)
    {
        if (isFatal)
        {
            _sink.Fail(reason);
            return;
        }

        _sink.WriteEvent(subscription, StreamItemKind.Disconnected, reason, default);
    }

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap) =>
        _sink.WriteEvent(subscription, StreamItemKind.Reconnected, WsDisconnectReason.None, in gap);

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) =>
        _sink.WriteEvent(subscription, StreamItemKind.Unsubscribed, WsDisconnectReason.None, default);
}
