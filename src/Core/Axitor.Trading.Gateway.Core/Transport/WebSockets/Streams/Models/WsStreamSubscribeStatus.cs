namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>How a subscribe request ended.</summary>
public enum WsStreamSubscribeStatus : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>The exchange confirmed the subscription.</summary>
    Subscribed,

    /// <summary>The stream was already subscribed; its existing handle is returned.</summary>
    AlreadySubscribed,

    /// <summary>
    /// No confirmation: the reply came too late, or the connection dropped first.
    /// The subscription stays registered: its data flows once the exchange applies it, and a reconnect subscribes it again.
    /// Unsubscribe to drop it.
    /// </summary>
    Unconfirmed,

    /// <summary>Every connection of the route is full. Raise MaxConnections first, then MaxStreamsPerConnection.</summary>
    CapacityExceeded,

    /// <summary>The exchange refused the subscription. Nothing was registered.</summary>
    Rejected,
}
