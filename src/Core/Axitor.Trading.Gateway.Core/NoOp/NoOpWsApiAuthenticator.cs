using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Core.NoOp;

/// <summary>For public WebSocket API requests: adds nothing, signs nothing.</summary>
internal readonly struct NoOpWsApiAuthenticator : IWsApiAuthenticator
{
    /// <inheritdoc />
    public static bool Signs => false;

    /// <inheritdoc />
    public void AddParams(ref WsApiRequestBuilder builder) { }

    /// <inheritdoc />
    public void AppendSignature(ref WsApiRequestBuilder builder) { }
}
