using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Account;

/// <summary>Account Information V3 Response Model.</summary>
public struct AccountInfoModel
{
    /// <summary>Margin assets, quote assets only. Empty rather than null when none are held.</summary>
    public AssetBalance[] Assets;

    /// <summary>Symbols carrying a position or a resting order. Empty rather than null when there are none.</summary>
    public AccountPosition[] Positions;

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
}

/// <summary>Asset Balance Model.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AssetBalance
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
}

/// <summary>Asset Position Model.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountPosition
{
    /// <summary>Trading symbol, e.g. BTCUSDT. Empty when the name does not fit Symbol.</summary>
    public Symbol Symbol;

    /// <summary>Last change to this position, milliseconds since the Unix epoch.</summary>
    public long UpdateTime;

    /// <summary>Position quantity in the base asset. Positive is long, negative is short, zero means the symbol is listed only because orders rest on it.</summary>
    public double Quantity;

    /// <summary>Unrealized profit at the current mark price.</summary>
    public double UnrealizedProfit;

    /// <summary>Margin assigned to the position when it is isolated. Zero under cross margin.</summary>
    public double IsolatedMargin;

    /// <summary>Position value at the current mark price, signed like Quantity.</summary>
    public double Notional;

    /// <summary>Wallet balance assigned to the isolated position.</summary>
    public double IsolatedWallet;

    /// <summary>Initial margin required at the current mark price.</summary>
    public double InitialMargin;

    /// <summary>Maintenance margin required. The figure liquidation is measured against.</summary>
    public double MaintenanceMargin;

    /// <summary>Which leg this is. Always PositionSide.BOTH in one-way mode.</summary>
    public PositionSide Side;
}
