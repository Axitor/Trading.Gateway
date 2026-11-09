using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>An order was created or changed: ORDER_TRADE_UPDATE.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned symbols from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct OrderUpdateModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Asset of the commission. Empty when the update has no commission.</summary>
    public Symbol CommissionAsset;

    /// <summary>
    /// Client order id. Exchange orders use their own: "autoclose-" for liquidation, "adl_autoclose" for ADL, "settlement_autoclose-" for settlement.
    /// Empty when it does not fit, which is reported to telemetry.
    /// </summary>
    public ClientOrderId ClientOrderId;

    /// <summary>Order id.</summary>
    public long OrderId;

    /// <summary>Trade id of the last fill. Zero when the update is not a fill.</summary>
    public long TradeId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Order trade time, milliseconds since the Unix epoch.</summary>
    public long TradeTime;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Zero for other orders.</summary>
    public long GoodTillDate;

    /// <summary>Original quantity. In contracts on COIN-M, as are the other quantities.</summary>
    public double Quantity;

    /// <summary>Original price.</summary>
    public double Price;

    /// <summary>Average fill price.</summary>
    public double AveragePrice;

    /// <summary>Stop price. Not used by trailing stop orders.</summary>
    public double StopPrice;

    /// <summary>Quantity of the last fill.</summary>
    public double LastFilledQuantity;

    /// <summary>Quantity filled so far.</summary>
    public double FilledQuantity;

    /// <summary>Price of the last fill.</summary>
    public double LastFilledPrice;

    /// <summary>Commission of the last fill.</summary>
    public double Commission;

    /// <summary>Notional of open bids.</summary>
    public double BidsNotional;

    /// <summary>Notional of open asks.</summary>
    public double AskNotional;

    /// <summary>Activation price of a trailing stop order.</summary>
    public double ActivationPrice;

    /// <summary>Callback rate of a trailing stop order.</summary>
    public double CallbackRate;

    /// <summary>Realized profit of the last fill.</summary>
    public double RealizedProfit;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Order type.</summary>
    public OrderType Type;

    /// <summary>Order type as placed, before a stop order triggered.</summary>
    public OrderType OriginalType;

    /// <summary>Time in force.</summary>
    public TimeInForce TimeInForce;

    /// <summary>What happened in this update.</summary>
    public ExecutionType ExecutionType;

    /// <summary>Status after this update.</summary>
    public OrderStatus Status;

    /// <summary>Price a stop order is triggered by.</summary>
    public WorkingType WorkingType;

    /// <summary>Position side.</summary>
    public PositionSide PositionSide;

    /// <summary>Why the order expired.</summary>
    public ExpiryReason ExpiryReason;

    /// <summary>Whether the last fill was on the maker side.</summary>
    public bool IsMaker;

    /// <summary>Whether the order is reduce-only.</summary>
    public bool IsReduceOnly;

    /// <summary>Whether a conditional order closes the whole position.</summary>
    public bool IsClosePosition;

    /// <summary>Whether price protection is on.</summary>
    public bool IsPriceProtect;
}
