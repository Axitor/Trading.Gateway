using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>An order as the exchange answered a place, cancel, modify, test or open orders request.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct OrderModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Client order id. Empty when it does not fit.</summary>
    public ClientOrderId ClientOrderId;

    /// <summary>Order id.</summary>
    public long OrderId;

    /// <summary>Creation time, milliseconds since the Unix epoch. Sent with open orders only; zero in other answers.</summary>
    public long CreateTime;

    /// <summary>Last change, milliseconds since the Unix epoch.</summary>
    public long UpdateTime;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Zero for other orders.</summary>
    public long GoodTillDate;

    /// <summary>Modification id given with a modify request. Zero otherwise.</summary>
    public long ModifyId;

    /// <summary>Original quantity.</summary>
    public double Quantity;

    /// <summary>Price.</summary>
    public double Price;

    /// <summary>Average fill price. Zero in answers to place, modify and cancel, which no longer carry it: take fills from user data.</summary>
    public double AveragePrice;

    /// <summary>Stop price. Not used by trailing stop orders.</summary>
    public double StopPrice;

    /// <summary>Quantity filled so far.</summary>
    public double FilledQuantity;

    /// <summary>Quote quantity filled so far. Zero in answers to place, modify and cancel, which no longer carry it: take fills from user data.</summary>
    public double FilledQuoteQuantity;

    /// <summary>Activation price of a trailing stop order.</summary>
    public double ActivationPrice;

    /// <summary>Callback rate of a trailing stop order.</summary>
    public double CallbackRate;

    /// <summary>Status.</summary>
    public OrderStatus Status;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Order type.</summary>
    public OrderType Type;

    /// <summary>Order type as placed.</summary>
    public OrderType OriginalType;

    /// <summary>Time in force.</summary>
    public TimeInForce TimeInForce;

    /// <summary>Position side.</summary>
    public PositionSide PositionSide;

    /// <summary>Price a stop order is triggered by.</summary>
    public WorkingType WorkingType;

    /// <summary>Price match mode.</summary>
    public PriceMatch PriceMatch;

    /// <summary>Self-trade prevention mode.</summary>
    public SelfTradePreventionMode SelfTradePreventionMode;

    /// <summary>Whether the order is reduce-only.</summary>
    public bool IsReduceOnly;

    /// <summary>Whether a conditional order closes the whole position.</summary>
    public bool IsClosePosition;

    /// <summary>Whether price protection is on.</summary>
    public bool IsPriceProtect;
}
