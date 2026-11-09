using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>Binds a request frame to its id and slot, so the connection can write it and stamp the send time.</summary>
internal readonly struct WsApiFrame<TFrame> : IPayloadWriter where TFrame : struct, IWsApiFrameWriter
{
    private readonly TFrame _frame;
    private readonly long _requestId;
    private readonly WsApiPendingSlot _slot;

    public WsApiFrame(TFrame frame, long requestId, WsApiPendingSlot slot)
    {
        _frame = frame;
        _requestId = requestId;
        _slot = slot;
    }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        _frame.Write(ref builder, _requestId);

        // Right before the socket send, so signing is not counted as network time.
        _slot.MarkSent(SysTime.GetTimestamp());
    }
}
