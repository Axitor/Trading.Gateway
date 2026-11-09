namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>What a ChannelSink item holds.</summary>
public enum StreamItemKind : byte
{
    /// <summary>A message of the subscription, in Model.</summary>
    Data,

    /// <summary>Data stopped; Reason says why. Nothing comes until Reconnected.</summary>
    Disconnected,

    /// <summary>Data flows again, but messages inside Gap are lost: rebuild any state made from them.</summary>
    Reconnected,

    /// <summary>The queue was full: DroppedCount messages inside Gap were dropped. Handle it like Reconnected.</summary>
    Overflow,

    /// <summary>You unsubscribed: items after this one under the same handle belong to another stream.</summary>
    Unsubscribed,
}
