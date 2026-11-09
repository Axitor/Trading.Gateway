using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Serialization;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;

/// <summary>Futures Account Balance V2 Endpoint Parser.</summary>
internal readonly struct AccountBalanceParser : IWsApiResultParser<AccountBalanceModel>
{
    /// <inheritdoc />
    public static AccountBalanceModel Parse(ReadOnlySpan<byte> result) =>
        new() { Assets = JsonSerializer.Deserialize(result, BinanceJsonContext.Default.AccountBalanceAssetArray) ?? [] };
}
