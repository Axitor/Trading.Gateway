using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Binance.Sinks;

/// <summary>Every message for one consumer: a full queue drops messages and says how many, but never holds up the receive loop.</summary>
public sealed class ChannelSinkTests
{
    [Fact]
    public void DeliversMessagesAndEventsInOrder()
    {
        ChannelSink<AggTradeModel> sink = new(capacity: 8, subscriptionCapacity: 2);
        WsStreamGap gap = new() { Reason = WsDisconnectReason.ExceptionSocket, StartUtcMillis = 10, EndUtcMillis = 20 };

        sink.Handler.OnData(0, Trade(1));
        sink.Handler.OnDisconnected(0, WsDisconnectReason.ExceptionSocket, isFatal: false);
        sink.Handler.OnReconnected(0, gap);
        sink.Handler.OnUnsubscribed(0);

        StreamItem<AggTradeModel> data = Read(sink);
        Assert.Equal(StreamItemKind.Data, data.Kind);
        Assert.Equal(1, data.Model.AggregateTradeId);

        StreamItem<AggTradeModel> disconnected = Read(sink);
        Assert.Equal(StreamItemKind.Disconnected, disconnected.Kind);
        Assert.Equal(WsDisconnectReason.ExceptionSocket, disconnected.Reason);

        StreamItem<AggTradeModel> reconnected = Read(sink);
        Assert.Equal(StreamItemKind.Reconnected, reconnected.Kind);
        Assert.Equal(gap, reconnected.Gap);

        Assert.Equal(StreamItemKind.Unsubscribed, Read(sink).Kind);
        Assert.False(sink.TryRead(out _));
    }

    [Fact]
    public void FullQueueReportsDroppedCountBeforeNextMessage()
    {
        ChannelSink<AggTradeModel> sink = new(capacity: 2, subscriptionCapacity: 2);

        for (int i = 1; i <= 5; i++)
        {
            sink.Handler.OnData(0, Trade(i));
        }

        Assert.Equal(3, sink.DroppedCount);
        Assert.Equal(1, Read(sink).Model.AggregateTradeId);
        Assert.Equal(2, Read(sink).Model.AggregateTradeId);

        // Another subscription lost nothing: no overflow for it.
        sink.Handler.OnData(1, Trade(100));
        StreamItem<AggTradeModel> other = Read(sink);
        Assert.Equal(StreamItemKind.Data, other.Kind);
        Assert.Equal(1, other.Subscription);

        sink.Handler.OnData(0, Trade(6));

        StreamItem<AggTradeModel> overflow = Read(sink);
        Assert.Equal(StreamItemKind.Overflow, overflow.Kind);
        Assert.Equal(0, overflow.Subscription);
        Assert.Equal(3, overflow.DroppedCount);
        Assert.True(overflow.Gap.StartUtcMillis <= overflow.Gap.EndUtcMillis);

        Assert.Equal(6, Read(sink).Model.AggregateTradeId);
    }

    [Fact]
    public void ConnectionEventsAreKeptWhenMessagesAreFull()
    {
        ChannelSink<AggTradeModel> sink = new(capacity: 1, subscriptionCapacity: 2);

        sink.Handler.OnData(0, Trade(1));
        sink.Handler.OnData(0, Trade(2));
        sink.Handler.OnData(0, Trade(3));
        sink.Handler.OnDisconnected(0, WsDisconnectReason.KeepAliveTimeout, isFatal: false);

        Assert.Equal(StreamItemKind.Data, Read(sink).Kind);

        StreamItem<AggTradeModel> overflow = Read(sink);
        Assert.Equal(StreamItemKind.Overflow, overflow.Kind);
        Assert.Equal(2, overflow.DroppedCount);

        Assert.Equal(StreamItemKind.Disconnected, Read(sink).Kind);
    }

    [Fact]
    public async Task FatalStopEndsQueueWithErrorAfterQueuedItems()
    {
        ChannelSink<AggTradeModel> sink = new(capacity: 8, subscriptionCapacity: 2);

        sink.Handler.OnData(0, Trade(1));
        sink.Handler.OnDisconnected(0, WsDisconnectReason.HandshakeBanLong, isFatal: true);

        Assert.Equal(1, Read(sink).Model.AggregateTradeId);

        WsStreamFatalException fatal = await Assert.ThrowsAsync<WsStreamFatalException>(() => sink.Completion);
        Assert.Equal(WsDisconnectReason.HandshakeBanLong, fatal.Disconnect.Reason);
    }

    [Fact]
    public async Task ConsumerThatStopsReadingEndsQueue()
    {
        // Room for one message and four events per subscription.
        ChannelSink<AggTradeModel> sink = new(capacity: 1, subscriptionCapacity: 1);

        for (int i = 0; i < 6; i++)
        {
            sink.Handler.OnReconnected(0, new WsStreamGap());
        }

        int read = 0;

        while (sink.TryRead(out _))
        {
            read++;
        }

        Assert.Equal(5, read);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.Completion);
    }

    private static AggTradeModel Trade(long id) => new() { AggregateTradeId = id, Price = 100, Quantity = 1 };

    private static StreamItem<AggTradeModel> Read(ChannelSink<AggTradeModel> sink)
    {
        Assert.True(sink.TryRead(out StreamItem<AggTradeModel> item));
        return item;
    }
}
