using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Routes events pushed on a WebSocket API connection and the gaps between connections. Implement as a readonly struct. Calls never overlap.</summary>
internal interface IWsApiEventDispatcher
{
    /// <summary>Handles one event on the receive loop; the epoch tells which connection it came on. Must return fast.</summary>
    WsDispatchResult Dispatch(ReadOnlySpan<byte> message, int connectionEpoch, out WsInboundSignal signal);

    /// <summary>The current connection of a lane dropped: events may be lost until OnReconnected. A fatal stop never resumes.</summary>
    void OnDisconnected(WsDisconnectReason reason, bool isFatal);

    /// <summary>A new connection passed the connection hook and serves; events inside the gap may be lost.</summary>
    void OnReconnected(in WsStreamGap gap);
}
