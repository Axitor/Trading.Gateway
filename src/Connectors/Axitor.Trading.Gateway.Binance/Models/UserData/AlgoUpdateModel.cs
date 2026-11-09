using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>An algo order, e.g. a stop loss or take profit, was created or changed: ALGO_UPDATE. USD-M only.</summary>
public struct AlgoUpdateModel
{
    /// <summary>Why the algo order failed, as the exchange words it. Null when there is no reason.</summary>
    public string? RejectReason;

    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Client algo id. Empty when it does not fit, which is reported to telemetry.</summary>
    public ClientOrderId ClientAlgoId;

    /// <summary>Algo order id.</summary>
    public long AlgoId;

    /// <summary>Id of the order placed in the matching engine. Zero until the algo order triggers.</summary>
    public long OrderId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>When the algo order triggered, milliseconds since the Unix epoch. Zero until then.</summary>
    public long TriggerTime;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Zero for other orders.</summary>
    public long GoodTillDate;

    /// <summary>Quantity.</summary>
    public double Quantity;

    /// <summary>Order price.</summary>
    public double Price;

    /// <summary>Trigger price.</summary>
    public double TriggerPrice;

    /// <summary>Average fill price in the matching engine. Zero until the algo order triggers.</summary>
    public double AveragePrice;

    /// <summary>Quantity filled in the matching engine. Zero until the algo order triggers.</summary>
    public double ExecutedQuantity;

    /// <summary>Status of the algo order.</summary>
    public AlgoStatus Status;

    /// <summary>Algo type.</summary>
    public AlgoType AlgoType;

    /// <summary>Order type.</summary>
    public OrderType Type;

    /// <summary>Order type in the matching engine. UNKNOWN until the algo order triggers.</summary>
    public OrderType ActualType;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Time in force.</summary>
    public TimeInForce TimeInForce;

    /// <summary>Price the trigger is checked against.</summary>
    public WorkingType WorkingType;

    /// <summary>Position side.</summary>
    public PositionSide PositionSide;

    /// <summary>Whether it closes the whole position.</summary>
    public bool IsClosePosition;

    /// <summary>Whether price protection is on.</summary>
    public bool IsPriceProtect;

    /// <summary>Whether the order is reduce-only.</summary>
    public bool IsReduceOnly;

    /// <summary>Whether a trailing stop order is activated. Meaningful only for trailing stop orders.</summary>
    public bool IsActivated;
}
