using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.Authentication;

/// <summary>Adds only the apiKey to a WebSocket API request, with no timestamp or signature. For USER_STREAM methods.</summary>
internal readonly struct BinanceWsApiKeyAuthenticator : IWsApiAuthenticator
{
    private readonly byte[] _apiKey;

    /// <summary>Creates an authenticator.</summary>
    internal BinanceWsApiKeyAuthenticator(byte[] apiKey) => _apiKey = apiKey;

    /// <inheritdoc />
    public static bool Signs => false;

    /// <inheritdoc />
    public void AddParams(ref WsApiRequestBuilder builder) => builder.AddAuthText("apiKey"u8, _apiKey);

    /// <inheritdoc />
    public void AppendSignature(ref WsApiRequestBuilder builder) { }
}
