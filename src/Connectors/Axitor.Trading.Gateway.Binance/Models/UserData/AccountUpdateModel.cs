using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>Balances or positions changed: ACCOUNT_UPDATE. Its balances and positions come as spans in the same call.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountUpdateHeader
{
    /// <summary>Symbol of a funding fee. Empty for other reasons.</summary>
    public Symbol FundingSymbol;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Why balances or positions changed.</summary>
    public AccountUpdateReason Reason;
}

/// <summary>Balance of one asset after the change.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountUpdateBalance
{
    /// <summary>Asset name, e.g. USDT or BTC.</summary>
    public Symbol Asset;

    /// <summary>Wallet balance.</summary>
    public double WalletBalance;

    /// <summary>Cross wallet balance.</summary>
    public double CrossWalletBalance;

    /// <summary>Change without profit and commission. A delta: adding it twice counts the change twice.</summary>
    public double BalanceChange;
}

/// <summary>A position after the change. USD-M sends only changed positions, COIN-M every position.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AccountUpdatePosition
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Position quantity: base asset on USD-M, contracts on COIN-M. Negative is short.</summary>
    public double Quantity;

    /// <summary>Entry price.</summary>
    public double EntryPrice;

    /// <summary>Breakeven price.</summary>
    public double BreakevenPrice;

    /// <summary>Realized profit so far, before fees.</summary>
    public double AccumulatedRealized;

    /// <summary>Unrealized profit.</summary>
    public double UnrealizedProfit;

    /// <summary>Wallet of an isolated position. Zero under cross margin.</summary>
    public double IsolatedWallet;

    /// <summary>Margin mode.</summary>
    public MarginType MarginType;

    /// <summary>Position side.</summary>
    public PositionSide Side;
}
