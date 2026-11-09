using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>One WebSocket API connection a request must go to, with the in-flight table of its lane.</summary>
internal struct WsApiConnectionTicket
{
    /// <summary>The connection.</summary>
    public WsConnection Connection;

    /// <summary>Requests in flight of the lane the connection belongs to.</summary>
    public WsApiPendingTable Table;
}
