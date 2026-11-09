using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Account;

/// <summary>Account information V2: balances and an entry for every symbol of the market. REST and the WebSocket API share it.</summary>
public struct AccountInfoV2Model
{
    /// <summary>Balances, one per asset. Empty rather than null when none are held.</summary>
    public AccountInfoV2Asset[] Assets;

    /// <summary>One entry per symbol of the market, with or without a position. Empty rather than null when there are none.</summary>
    public AccountInfoV2Position[] Positions;

    /// <summary>Trade group id, -1 when the account is in none.</summary>
    public long TradeGroupId;

    /// <summary>Total initial margin required across positions and resting orders.</summary>
    public double TotalInitialMargin;

    /// <summary>Total maintenance margin required.</summary>
    public double TotalMaintenanceMargin;

    /// <summary>Total wallet balance.</summary>
    public double TotalWalletBalance;

    /// <summary>Total unrealized profit.</summary>
    public double TotalUnrealizedProfit;

    /// <summary>Total margin balance: wallet balance plus unrealized profit.</summary>
    public double TotalMarginBalance;

    /// <summary>Initial margin held by open positions.</summary>
    public double TotalPositionInitialMargin;

    /// <summary>Initial margin held by resting orders.</summary>
    public double TotalOpenOrderInitialMargin;

    /// <summary>Total cross wallet balance.</summary>
    public double TotalCrossWalletBalance;

    /// <summary>Total unrealized profit of cross positions.</summary>
    public double TotalCrossUnrealizedProfit;

    /// <summary>Balance available for new orders.</summary>
    public double AvailableBalance;

    /// <summary>Largest amount transferable out without breaching margin requirements.</summary>
    public double MaxWithdrawAmount;

    /// <summary>Commission tier.</summary>
    public int FeeTier;

    /// <summary>Whether the BNB fee discount is on. False on the WebSocket API, which does not send it.</summary>
    public bool FeeBurn;

    /// <summary>Whether trading is allowed.</summary>
    public bool CanTrade;

    /// <summary>Whether transfers in are allowed.</summary>
    public bool CanDeposit;

    /// <summary>Whether transfers out are allowed.</summary>
    public bool CanWithdraw;

    /// <summary>Whether Multi-Assets mode is on.</summary>
    public bool IsMultiAssetsMargin;
}

/// <summary>Balance of one asset.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountInfoV2Asset
{
    /// <summary>Asset name, e.g. USDT. Empty when the name does not fit Symbol.</summary>
    public Symbol Asset;

    /// <summary>Last change to this asset, milliseconds since the Unix epoch.</summary>
    public long UpdateTime;

    /// <summary>Wallet balance.</summary>
    public double WalletBalance;

    /// <summary>Unrealized profit across positions margined in this asset.</summary>
    public double UnrealizedProfit;

    /// <summary>Margin balance: wallet balance plus unrealized profit.</summary>
    public double MarginBalance;

    /// <summary>Maintenance margin required.</summary>
    public double MaintenanceMargin;

    /// <summary>Total initial margin required at the current mark price.</summary>
    public double InitialMargin;

    /// <summary>Initial margin held by open positions.</summary>
    public double PositionInitialMargin;

    /// <summary>Initial margin held by resting orders.</summary>
    public double OpenOrderInitialMargin;

    /// <summary>Cross wallet balance.</summary>
    public double CrossWalletBalance;

    /// <summary>Unrealized profit of cross positions.</summary>
    public double CrossUnrealizedProfit;

    /// <summary>Balance available for new orders.</summary>
    public double AvailableBalance;

    /// <summary>Largest amount transferable out without breaching margin requirements.</summary>
    public double MaxWithdrawAmount;

    /// <summary>Whether the asset can be used as margin in Multi-Assets mode.</summary>
    public bool MarginAvailable;
}

/// <summary>Position of one symbol, with its leverage and margin mode. Listed even with no position.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountInfoV2Position
{
    /// <summary>Trading symbol, e.g. BTCUSDT. Empty when the name does not fit Symbol.</summary>
    public Symbol Symbol;

    /// <summary>Last change to this position, milliseconds since the Unix epoch.</summary>
    public long UpdateTime;

    /// <summary>Position quantity in the base asset. Positive is long, negative is short.</summary>
    public double Quantity;

    /// <summary>Average entry price.</summary>
    public double EntryPrice;

    /// <summary>Breakeven price. Zero on REST, which does not send it.</summary>
    public double BreakEvenPrice;

    /// <summary>Unrealized profit at the current mark price.</summary>
    public double UnrealizedProfit;

    /// <summary>Initial margin required at the current mark price.</summary>
    public double InitialMargin;

    /// <summary>Maintenance margin required. The figure liquidation is measured against.</summary>
    public double MaintenanceMargin;

    /// <summary>Initial margin held by the position.</summary>
    public double PositionInitialMargin;

    /// <summary>Initial margin held by resting orders.</summary>
    public double OpenOrderInitialMargin;

    /// <summary>Largest position notional allowed at the current leverage.</summary>
    public double MaxNotional;

    /// <summary>Initial leverage.</summary>
    public int Leverage;

    /// <summary>Which leg this is. Always PositionSide.BOTH in one-way mode.</summary>
    public PositionSide Side;

    /// <summary>Whether the position uses isolated margin.</summary>
    public bool IsIsolated;
}
