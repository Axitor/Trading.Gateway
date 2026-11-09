namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>Result of a subscribe request.</summary>
public struct WsStreamSubscribeResult
{
    /// <summary>Subscription handle, or -1 when nothing was registered.</summary>
    public int Handle;

    /// <summary>How the request ended.</summary>
    public WsStreamSubscribeStatus Status;
}
