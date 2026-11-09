using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;

/// <summary>Futures Account Balance V2 Endpoint PayLoad Writer. Carries only the auth params.</summary>
internal readonly struct AccountBalancePayload : IWsApiRequestWriter
{
    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "v2/account.balance"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) { }
}
