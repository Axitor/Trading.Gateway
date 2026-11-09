namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>Which stream connection an event is about.</summary>
public struct WsStreamConnectionInfo
{
    /// <summary>Route the connection goes to.</summary>
    public Uri Uri;

    /// <summary>Index of the connection within its route.</summary>
    public int Shard;

    /// <summary>Sequence number of the connection within the transport.</summary>
    public int Epoch;
}
