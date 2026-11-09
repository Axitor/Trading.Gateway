using System.Net.Sockets;
using System.Net.WebSockets;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>Why a connection ended or a connect attempt failed, with diagnostics.</summary>
public struct WsDisconnectInfo
{
    /// <summary>Why.</summary>
    public WsDisconnectReason Reason;

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

    /// <summary>Exception thrown by a message handler, for DispatchFault.</summary>
    public Exception? Fault;
}
