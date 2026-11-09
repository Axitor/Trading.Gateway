using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountInfo;

/// <summary>Account Information Endpoint PayLoad Writer. Carries only the auth params.</summary>
internal readonly struct AccountInfoPayload : IWsApiRequestWriter
{
    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "account.status"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) { }
}
