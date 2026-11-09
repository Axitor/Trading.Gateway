namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>How a subscribe or unsubscribe request ended.</summary>
internal struct WsStreamControlReply
{
    /// <summary>Exchange error code of a refused request.</summary>
    public int ExchangeCode;

    /// <summary>Whether a reply came. False when the connection dropped first.</summary>
    public bool Received;

    /// <summary>Whether the exchange refused the request.</summary>
    public bool IsError;
}
