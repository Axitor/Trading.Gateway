using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>Result of waiting for a response.</summary>
internal struct WsApiPendingCompletion
{
    /// <summary>How the wait ended.</summary>
    public WsApiPendingOutcome Outcome;

    /// <summary>Why the connection ended, for ConnectionLost and AppShutdown.</summary>
    public WsDisconnectReason Reason;
}
