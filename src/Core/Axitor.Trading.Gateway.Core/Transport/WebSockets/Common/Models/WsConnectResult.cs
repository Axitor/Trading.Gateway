using System.Net.Sockets;
using System.Net.WebSockets;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>Outcome of one connect attempt.</summary>
internal struct WsConnectResult
{
    /// <summary>The open connection, or null when the attempt failed.</summary>
    public WsConnection? Connection;

    /// <summary>Handshake HTTP status, or zero when no response was received.</summary>
    public int StatusCode;

    /// <summary>Retry-After of a rejected handshake, or zero when absent.</summary>
    public int RetryAfterSeconds;

    /// <summary>HttpRequestError for DNS, TLS, protocol, etc.</summary>
    public HttpRequestError HttpError;

    /// <summary>WebSocketError of a protocol failure.</summary>
    public WebSocketError WebSocketError;

    /// <summary>SocketError for nested-socket breaks.</summary>
    public SocketError SocketError;

    /// <summary>Why the attempt failed. None on success.</summary>
    public WsDisconnectReason Reason;

}
