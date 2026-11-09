using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;

/// <summary>Subscribe to User Data Stream through signature subscription. Carries only the auth params.</summary>
internal readonly struct SubscribePayload : IWsApiRequestWriter
{
    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "userDataStream.subscribe.signature"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) { }
}
