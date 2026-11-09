namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>What kind of message arrived.</summary>
internal enum WsInboundKind : byte
{
    /// <summary>Not recognized. Dropped and reported to telemetry.</summary>
    Unknown,

    /// <summary>A reply to our request, matched by its id.</summary>
    Response,

    /// <summary>Data pushed by the server: stream data or a WebSocket API event.</summary>
    Push,
}
