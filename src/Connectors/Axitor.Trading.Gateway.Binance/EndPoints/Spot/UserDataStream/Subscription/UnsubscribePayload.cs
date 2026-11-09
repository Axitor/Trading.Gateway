using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;

/// <summary>Unsubscribe from User Data Stream. No subscription id: the connection holds only ours, and every subscription on it ends.</summary>
internal readonly struct UnsubscribePayload : IWsApiRequestWriter
{
    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "userDataStream.unsubscribe"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) { }
}
