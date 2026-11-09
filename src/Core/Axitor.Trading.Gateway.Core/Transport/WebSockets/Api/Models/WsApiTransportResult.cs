using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>What the transport hands back for one request. When Slot is set, call Slot.Release once done with it.</summary>
internal struct WsApiTransportResult
{
    /// <summary>Holds the response in Message. Null when the request never got a slot.</summary>
    public WsApiPendingSlot? Slot;

    /// <summary>How the request ended.</summary>
    public WsApiPendingCompletion Completion;

    /// <summary>Id the request was sent with, or zero when it never got a slot.</summary>
    public long RequestId;
}
