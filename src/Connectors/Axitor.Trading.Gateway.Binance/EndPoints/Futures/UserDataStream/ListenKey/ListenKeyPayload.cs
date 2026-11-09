using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;

/// <summary>Start User Data Stream Endpoint PayLoad Writer. Carries only the apiKey.</summary>
internal readonly struct ListenKeyPayload : IWsApiRequestWriter
{
    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "userDataStream.start"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) { }
}
