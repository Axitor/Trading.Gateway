using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>A period with no stream data.</summary>
public struct WsStreamGap
{
    /// <summary>Why the connection broke.</summary>
    public WsDisconnectReason Reason;

    /// <summary>When data stopped, milliseconds since the Unix epoch.</summary>
    public long StartUtcMillis;

    /// <summary>When data resumed, milliseconds since the Unix epoch.</summary>
    public long EndUtcMillis;
}
