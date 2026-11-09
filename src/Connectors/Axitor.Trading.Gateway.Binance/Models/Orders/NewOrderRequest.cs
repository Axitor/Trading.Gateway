using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>A new LIMIT or MARKET order. Zero or UNKNOWN leaves a field out of the request.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NewOrderRequest
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public required Symbol Symbol;

    /// <summary><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='clientOrderId']/node()"/></summary>
    public required ClientOrderId ClientOrderId;

    /// <summary><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='filters']/node()"/></summary>
    public required SymbolFilters Filters;

    /// <summary>Quantity in the base asset, rounded down to the step before sending.</summary>
    public double Quantity;

    /// <summary>Limit price, rounded to the nearest tick before sending. Zero for MARKET or with PriceMatch.</summary>
    public double Price;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Required for GTD.</summary>
    public long GoodTillDate;

    /// <summary>Side.</summary>
    public required OrderSide Side;

    /// <summary>LIMIT or MARKET. Stop, take profit and trailing stop orders go through the algo order endpoint.</summary>
    public required OrderType Type;

    /// <summary>Time in force. Required for LIMIT.</summary>
    public TimeInForce TimeInForce;

    /// <summary>BOTH in one-way mode, LONG or SHORT in hedge mode.</summary>
    public PositionSide PositionSide;

    /// <summary>Takes a LIMIT price from the order book instead of Price.</summary>
    public PriceMatch PriceMatch;

    /// <summary>What expires on a self-trade. Left out, the exchange uses its default.</summary>
    public SelfTradePreventionMode SelfTradePreventionMode;

    /// <summary>ACK by default. RESULT waits for the final status of a MARKET or IOC/FOK order.</summary>
    public OrderResponseType ResponseType;

    /// <summary>Only reduces the position and skips the min notional check. Not allowed in hedge mode.</summary>
    public bool IsReduceOnly;
}
