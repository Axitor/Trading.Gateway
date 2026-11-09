using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>Position risk is too high: MARGIN_CALL. Its positions come as a span in the same call.</summary>
public struct MarginCallHeader
{
    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Cross wallet balance. Zero unless a cross position is called.</summary>
    public double CrossWalletBalance;
}

/// <summary>A position under the margin call.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct MarginCallPosition
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Position quantity: base asset on USD-M, contracts on COIN-M. Negative is short.</summary>
    public double Quantity;

    /// <summary>Mark price.</summary>
    public double MarkPrice;

    /// <summary>Unrealized profit.</summary>
    public double UnrealizedProfit;

    /// <summary>Maintenance margin required.</summary>
    public double MaintenanceMargin;

    /// <summary>Wallet of an isolated position. Zero under cross margin.</summary>
    public double IsolatedWallet;

    /// <summary>Margin mode.</summary>
    public MarginType MarginType;

    /// <summary>Position side.</summary>
    public PositionSide Side;
}
