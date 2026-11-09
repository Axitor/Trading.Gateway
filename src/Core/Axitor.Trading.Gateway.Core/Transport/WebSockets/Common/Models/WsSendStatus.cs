namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>Whether an outgoing message reached the wire.</summary>
internal enum WsSendStatus : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Handed to the socket in full.</summary>
    Sent,

    /// <summary>Never left: the connection was not open. Safe to treat as not placed.</summary>
    NotSent,

    /// <summary>Failed midway. The exchange may or may not have received it.</summary>
    Unknown,
}
