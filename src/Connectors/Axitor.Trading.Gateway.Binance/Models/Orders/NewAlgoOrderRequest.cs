using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>A new conditional algo order: stop, take profit or trailing stop. Zero or UNKNOWN leaves a field out of the request.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NewAlgoOrderRequest
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public required Symbol Symbol;

    /// <summary><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='clientAlgoId']/node()"/></summary>
    public required ClientOrderId ClientAlgoId;

    /// <summary><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='filters']/node()"/></summary>
    public required SymbolFilters Filters;

    /// <summary>Quantity in the base asset, rounded down to the step before sending. Left out with IsClosePosition.</summary>
    public double Quantity;

    /// <summary>Limit price of STOP and TAKE_PROFIT, rounded to the nearest tick before sending.</summary>
    public double Price;

    /// <summary>Price that triggers the order, rounded to the nearest tick before sending.</summary>
    public double TriggerPrice;

    /// <summary>Price that activates a TRAILING_STOP_MARKET order, rounded to the nearest tick. Zero uses the latest price.</summary>
    public double ActivatePrice;

    /// <summary>Callback rate of a TRAILING_STOP_MARKET order, in percent, from 0.1 to 10.</summary>
    public double CallbackRate;

    /// <summary>When a GTD order is canceled, milliseconds since the Unix epoch. Required for GTD.</summary>
    public long GoodTillDate;

    /// <summary>Side.</summary>
    public required OrderSide Side;

    /// <summary>STOP_MARKET, TAKE_PROFIT_MARKET, STOP, TAKE_PROFIT or TRAILING_STOP_MARKET.</summary>
    public required OrderType Type;

    /// <summary>Time in force of STOP and TAKE_PROFIT. Left out, the exchange uses GTC.</summary>
    public TimeInForce TimeInForce;

    /// <summary>BOTH in one-way mode, LONG or SHORT in hedge mode.</summary>
    public PositionSide PositionSide;

    /// <summary>Price the trigger is checked against. Left out, the exchange uses CONTRACT_PRICE.</summary>
    public WorkingType WorkingType;

    /// <summary>Takes the price of STOP and TAKE_PROFIT from the order book instead of Price.</summary>
    public PriceMatch PriceMatch;

    /// <summary>What expires on a self-trade. Left out, the exchange uses NONE.</summary>
    public SelfTradePreventionMode SelfTradePreventionMode;

    /// <summary>ACK by default.</summary>
    public OrderResponseType ResponseType;

    /// <summary>Closes the whole position when triggered, for STOP_MARKET and TAKE_PROFIT_MARKET. Quantity and IsReduceOnly are left out.</summary>
    public bool IsClosePosition;

    /// <summary>At trigger, the gap between mark and last price must stay within the symbol's triggerProtect. For STOP_MARKET and TAKE_PROFIT_MARKET.</summary>
    public bool IsPriceProtect;

    /// <summary>Only reduces the position and skips the min notional check. Not allowed in hedge mode.</summary>
    public bool IsReduceOnly;
}
