namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

/// <summary>What the core must do after a pushed message was dispatched.</summary>
internal enum WsInboundSignal : byte
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The server is about to drop the connection; replace it now.</summary>
    ServerShutdown,
}
