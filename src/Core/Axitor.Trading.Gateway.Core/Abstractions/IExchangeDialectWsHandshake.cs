using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Everything about a WebSocket handshake that differs between exchanges.</summary>
internal interface IExchangeDialectWsHandshake
{
    /// <summary>Classifies a rejected handshake by its HTTP status and reads Retry-After from its headers.</summary>
    static abstract void ReadHandshakeFailure(int statusCode, IReadOnlyDictionary<string, IEnumerable<string>>? headers, out WsDisconnectReason reason, out int retryAfterSeconds);
}
