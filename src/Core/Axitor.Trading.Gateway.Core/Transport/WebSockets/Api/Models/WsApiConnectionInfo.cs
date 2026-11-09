using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>Which WebSocket API connection an event is about.</summary>
public struct WsApiConnectionInfo
{
    /// <summary>Endpoint the connection goes to.</summary>
    public Uri Uri;

    /// <summary>Lane the connection serves.</summary>
    public RequestPriority Lane;

    /// <summary>Sequence number of the connection within the transport.</summary>
    public int Epoch;
}
