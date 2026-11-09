namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>Lifecycle of one WebSocket connection.</summary>
internal enum WsConnectionState : byte
{
    /// <summary>Sends and receives.</summary>
    Open,

    /// <summary>Being rotated out: takes no new sends, still receives until in-flight requests settle.</summary>
    Draining,

    /// <summary>Closed or aborted. Nothing is sent or received.</summary>
    Closed,
}
