using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.NoOp;

/// <summary>For WebSocket APIs that push no events: any event is reported as unknown.</summary>
internal readonly struct NoOpWsApiEventDispatcher : IWsApiEventDispatcher
{
    /// <inheritdoc />
    public WsDispatchResult Dispatch(ReadOnlySpan<byte> message, int connectionEpoch, out WsInboundSignal signal)
    {
        signal = WsInboundSignal.None;
        return WsDispatchResult.Unreadable;
    }

    /// <inheritdoc />
    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) { }

    /// <inheritdoc />
    public void OnReconnected(in WsStreamGap gap) { }
}
