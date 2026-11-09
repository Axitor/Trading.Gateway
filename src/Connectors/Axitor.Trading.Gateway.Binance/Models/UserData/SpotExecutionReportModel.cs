using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>A spot order was created or changed: executionReport. Average fill price is FilledQuoteQuantity / FilledQuantity.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned symbols from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotExecutionReportModel
{
    /// <summary>Why the order was rejected, as the exchange words it. Null when it was not.</summary>
    public string? RejectReason;

    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Asset of the commission. Empty when the update has no commission.</summary>
    public Symbol CommissionAsset;

    /// <summary>Symbol of the counter order of a self-trade prevention. Empty otherwise.</summary>
    public Symbol CounterSymbol;

    /// <summary>Client order id. Empty when it does not fit, which is reported to telemetry.</summary>
    public ClientOrderId ClientOrderId;

    /// <summary>Client order id of the order being canceled. Empty otherwise.</summary>
    public ClientOrderId OriginalClientOrderId;

    /// <summary>Order id.</summary>
    public long OrderId;

    /// <summary>Order list id, or -1 when the order is not in a list.</summary>
    public long OrderListId;

    /// <summary>Trade id of the last fill, or -1 when the update is not a fill.</summary>
    public long TradeId;

    /// <summary>Execution id.</summary>
    public long ExecutionId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Order creation time, milliseconds since the Unix epoch.</summary>
    public long CreateTime;

    /// <summary>When the order started working on the book, milliseconds since the Unix epoch. Zero until then.</summary>
    public long WorkingTime;

    /// <summary>When a trailing stop activated, milliseconds since the Unix epoch. Zero for other orders.</summary>
    public long TrailingTime;

    /// <summary>Strategy id given on placement. Zero when none.</summary>
    public long StrategyId;

    /// <summary>Prevented match id of a self-trade prevention. Zero otherwise.</summary>
    public long PreventedMatchId;

    /// <summary>Trade group id of a self-trade prevention. Zero otherwise.</summary>
    public long TradeGroupId;

    /// <summary>Order id of the counter order of a self-trade prevention. Zero otherwise.</summary>
    public long CounterOrderId;

    /// <summary>Order quantity.</summary>
    public double Quantity;

    /// <summary>Order price.</summary>
    public double Price;

    /// <summary>Stop price.</summary>
    public double StopPrice;

    /// <summary>Iceberg quantity.</summary>
    public double IcebergQuantity;

    /// <summary>Quantity of the last fill.</summary>
    public double LastFilledQuantity;

    /// <summary>Quantity filled so far.</summary>
    public double FilledQuantity;

    /// <summary>Price of the last fill.</summary>
    public double LastFilledPrice;

    /// <summary>Commission of the last fill.</summary>
    public double Commission;

    /// <summary>Quote quantity filled so far.</summary>
    public double FilledQuoteQuantity;

    /// <summary>Quote quantity of the last fill.</summary>
    public double LastFilledQuoteQuantity;

    /// <summary>Quote order quantity given on placement.</summary>
    public double QuoteOrderQuantity;

    /// <summary>Quantity prevented by self-trade prevention so far. Zero otherwise.</summary>
    public double PreventedQuantity;

    /// <summary>Quantity prevented by the last self-trade prevention. Zero otherwise.</summary>
    public double LastPreventedQuantity;

    /// <summary>Quantity that would have traded without self-trade prevention. Zero otherwise.</summary>
    public double PreventedExecutionQuantity;

    /// <summary>Price that would have traded without self-trade prevention. Zero otherwise.</summary>
    public double PreventedExecutionPrice;

    /// <summary>Quote quantity that would have traded without self-trade prevention. Zero otherwise.</summary>
    public double PreventedExecutionQuoteQuantity;

    /// <summary>Price of a pegged order. Zero for other orders.</summary>
    public double PeggedPrice;

    /// <summary>Trailing delta of a trailing stop. Zero for other orders.</summary>
    public int TrailingDelta;

    /// <summary>Strategy type given on placement. Zero when none.</summary>
    public int StrategyType;

    /// <summary>Peg offset in price levels. Zero for other orders.</summary>
    public int PegOffsetValue;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Order type.</summary>
    public SpotOrderType Type;

    /// <summary>Time in force.</summary>
    public TimeInForce TimeInForce;

    /// <summary>What happened in this update.</summary>
    public SpotExecutionType ExecutionType;

    /// <summary>Status after this update.</summary>
    public SpotOrderStatus Status;

    /// <summary>Self-trade prevention mode.</summary>
    public SpotSelfTradePreventionMode SelfTradePreventionMode;

    /// <summary>Why the order expired. NONE when it did not.</summary>
    public SpotExpiryReason ExpiryReason;

    /// <summary>Where the order works. UNKNOWN when not sent.</summary>
    public SpotWorkingFloor WorkingFloor;

    /// <summary>Peg price type. UNKNOWN for orders that are not pegged.</summary>
    public SpotPegPriceType PegPriceType;

    /// <summary>Peg offset type. UNKNOWN for orders that are not pegged.</summary>
    public SpotPegOffsetType PegOffsetType;

    /// <summary>Whether the order is on the book.</summary>
    public bool IsWorking;

    /// <summary>Whether the last fill was on the maker side.</summary>
    public bool IsMaker;

    /// <summary>Whether the order used smart order routing.</summary>
    public bool UsedSor;
}
