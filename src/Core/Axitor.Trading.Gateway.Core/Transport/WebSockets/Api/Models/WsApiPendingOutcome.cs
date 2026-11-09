namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>How the wait for a response ended.</summary>
internal enum WsApiPendingOutcome : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>The response arrived.</summary>
    Response,

    /// <summary>Never reached the wire, e.g. the connection died while the request waited for the message budget. No order check needed.</summary>
    NotSent,

    /// <summary>No response within the response timeout. The exchange may have applied the request.</summary>
    Timeout,

    /// <summary>The connection broke first. The exchange may have applied the request.</summary>
    ConnectionLost,

    /// <summary>The application is shutting down.</summary>
    AppShutdown,

    /// <summary>No open connection to send on. Never reached the wire.</summary>
    NoConnection,

    /// <summary>Every in-flight slot was busy. Never reached the wire.</summary>
    InFlightLimit,
}
