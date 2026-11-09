using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>One item of a ChannelSink: a message or a connection event of a subscription.</summary>
public struct StreamItem<TModel> where TModel : struct
{
    /// <summary>The message. Set for Data only.</summary>
    public TModel Model;

    /// <summary>The period with lost messages. Set for Reconnected and Overflow.</summary>
    public WsStreamGap Gap;

    /// <summary>Messages dropped because the queue was full. Set for Overflow only.</summary>
    public long DroppedCount;

    /// <summary>Subscription handle the item belongs to.</summary>
    public int Subscription;

    /// <summary>Why data stopped. Set for Disconnected only.</summary>
    public WsDisconnectReason Reason;

    /// <summary>What the item holds.</summary>
    public StreamItemKind Kind;
}
