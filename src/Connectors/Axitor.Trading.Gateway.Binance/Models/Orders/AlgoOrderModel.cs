using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>An algo order, e.g. a stop loss or take profit, as the exchange answered a place or open algo orders request.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AlgoOrderModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Client algo id. Empty when it does not fit.</summary>
    public ClientOrderId ClientAlgoId;

    /// <summary>Algo order id.</summary>
    public long AlgoId;

    /// <summary>Id of the order placed in the matching engine. Zero until the algo order triggers; sent with open algo orders only.</summary>
    public long ActualOrderId;

    /// <summary>Creation time, milliseconds since the Unix epoch.</summary>
    public long CreateTime;

    /// <summary>Last change, milliseconds since the Unix epoch.</summary>
    public long UpdateTime;

    /// <summary>When the algo order triggered, milliseconds since the Unix epoch. Zero until then.</summary>
    public long TriggerTime;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Zero for other orders.</summary>
    public long GoodTillDate;

    /// <summary>Quantity. Zero for a close-position order.</summary>
    public double Quantity;

    /// <summary>Order price. Zero for market orders.</summary>
    public double Price;

    /// <summary>Trigger price.</summary>
    public double TriggerPrice;

    /// <summary>Activation price of a trailing stop order. Zero for other orders.</summary>
    public double ActivationPrice;

    /// <summary>Callback rate of a trailing stop order. Zero for other orders.</summary>
    public double CallbackRate;

    /// <summary>Average fill price in the matching engine. Zero until it fills; sent with open algo orders only.</summary>
    public double ActualPrice;

    /// <summary>Status of the algo order.</summary>
    public AlgoStatus Status;

    /// <summary>Algo type.</summary>
    public AlgoType AlgoType;

    /// <summary>Order type.</summary>
    public OrderType Type;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Position side.</summary>
    public PositionSide PositionSide;

    /// <summary>Time in force.</summary>
    public TimeInForce TimeInForce;

    /// <summary>Price the trigger is checked against.</summary>
    public WorkingType WorkingType;

    /// <summary>Price match mode.</summary>
    public PriceMatch PriceMatch;

    /// <summary>Self-trade prevention mode.</summary>
    public SelfTradePreventionMode SelfTradePreventionMode;

    /// <summary>Whether it closes the whole position.</summary>
    public bool IsClosePosition;

    /// <summary>Whether price protection is on.</summary>
    public bool IsPriceProtect;

    /// <summary>Whether the order is reduce-only.</summary>
    public bool IsReduceOnly;
}
