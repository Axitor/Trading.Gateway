using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Routes stream data and connection breaks to the connector's handler. Implement as a readonly struct.</summary>
internal interface IWsStreamDispatcher
{
    /// <summary>Handles the data of one subscription on the receive loop. Must return fast.</summary>
    WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data);

    /// <summary>Handles a message the exchange pushed outside any subscription. Must return fast.</summary>
    WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message);

    /// <summary>Data of the subscription stopped; nothing arrives until OnReconnected.</summary>
    void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal);

    /// <summary>Data of the subscription flows again after a gap.</summary>
    void OnReconnected(int subscription, in WsStreamGap gap);

    /// <summary>A stream is about to be subscribed under this handle; drop any state left from its previous stream.</summary>
    void OnSubscribing(int subscription);

    /// <summary>The subscription was removed by an unsubscribe; nothing arrives for it after this call, and its handle may be reused.</summary>
    void OnUnsubscribed(int subscription);
}
