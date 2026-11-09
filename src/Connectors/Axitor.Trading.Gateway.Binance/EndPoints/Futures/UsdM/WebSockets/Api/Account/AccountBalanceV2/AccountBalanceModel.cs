using System.Text.Json.Serialization;
using Axitor.Trading.Gateway.Core.Models.Converters;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;

/// <summary>Futures Account Balance V2 Response Model.</summary>
public struct AccountBalanceModel
{
    /// <summary>Balances, one per asset. Empty rather than null when none are held.</summary>
    public AccountBalanceAsset[] Assets;
}

/// <summary>Balance of one asset.</summary>
public struct AccountBalanceAsset
{
    /// <summary>Asset name, e.g. USDT.</summary>
    [JsonPropertyName("asset")]
    [JsonConverter(typeof(SymbolConverter))]
    public Symbol Asset;

    /// <summary>Last change to this asset, milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("updateTime")]
    public long UpdateTime;

    /// <summary>Wallet balance.</summary>
    [JsonPropertyName("balance")]
    public double WalletBalance;

    /// <summary>Cross wallet balance.</summary>
    [JsonPropertyName("crossWalletBalance")]
    public double CrossWalletBalance;

    /// <summary>Unrealized profit of cross positions.</summary>
    [JsonPropertyName("crossUnPnl")]
    public double CrossUnrealizedProfit;

    /// <summary>Balance available for new orders.</summary>
    [JsonPropertyName("availableBalance")]
    public double AvailableBalance;

    /// <summary>Largest amount transferable out.</summary>
    [JsonPropertyName("maxWithdrawAmount")]
    public double MaxWithdrawAmount;

    /// <summary>Whether the asset can be used as margin in Multi-Assets mode.</summary>
    [JsonPropertyName("marginAvailable")]
    public bool MarginAvailable;
}
