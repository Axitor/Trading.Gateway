namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>Why a WebSocket connection is not open.</summary>
public enum WsDisconnectReason : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Handshake refused with a short-term ban. Back off by Retry-After.</summary>
    HandshakeBanShort,

    /// <summary>Handshake refused with a long-term ban.</summary>
    HandshakeBanLong,

    /// <summary>Handshake refused in a way retrying cannot fix, e.g. a bad path or a missing key.</summary>
    HandshakeRejected,

    /// <summary>Handshake answered with a server-side error, e.g. during maintenance.</summary>
    HandshakeUnavailable,

    /// <summary>The connection was not established within the connect timeout.</summary>
    ConnectTimeout,

    /// <summary>Handshake failed before an HTTP response: DNS, TLS or protocol. See HttpError.</summary>
    ExceptionHttp,

    /// <summary>Socket-level failure. See SocketError.</summary>
    ExceptionSocket,

    /// <summary>The stream broke without a socket error, e.g. TLS cut mid-frame.</summary>
    ExceptionIO,

    /// <summary>WebSocket protocol failure. See WebSocketError.</summary>
    ExceptionWebSocket,

    /// <summary>The server sent a close frame.</summary>
    ClosedByServer,

    /// <summary>No pong came back within the keep-alive timeout.</summary>
    KeepAliveTimeout,

    /// <summary>An incoming message exceeded MaxMessageBytes.</summary>
    MessageTooBig,

    /// <summary>The server announced shutdown. Replaced by a new connection first.</summary>
    ServerShutdown,

    /// <summary>Planned rotation by connection age. Replaced by a new connection first.</summary>
    Rotation,

    /// <summary>The application is shutting down.</summary>
    AppShutdown,

    /// <summary>A message handler threw. Fatal: a bug that retrying cannot fix.</summary>
    DispatchFault,

    /// <summary>The exchange answered a request with a fatal error, e.g. a long ban or a rejected key.</summary>
    ExchangeFatal,

    /// <summary>The watchdog stream went quiet. The network is checked before reconnecting.</summary>
    WatchdogTimeout,

    /// <summary>Replaced after a network repair: it may have died quietly during the outage. A new connection takes over first.</summary>
    NetworkRepaired,

    /// <summary>The exchange ended the subscription, e.g. its listen key expired. Data flows again once it is renewed.</summary>
    SubscriptionExpired,

    /// <summary>A message that must not be lost could not be read, e.g. the exchange changed its format. Fatal.</summary>
    MessageUnreadable,

    /// <summary>A new connection could not be prepared, e.g. a subscription on it was not confirmed. Another one replaces it.</summary>
    ConnectionHookFailed,
}
