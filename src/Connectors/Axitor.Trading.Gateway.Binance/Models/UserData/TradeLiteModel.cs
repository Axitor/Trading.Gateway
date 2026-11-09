using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.UserData;

/// <summary>An order was filled: TRADE_LITE, USD-M only. Comes before the ORDER_TRADE_UPDATE of the same fill, with no commission or realized profit.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TradeLiteModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>
    /// Client order id. Exchange orders use their own: "autoclose-" for liquidation, "adl_autoclose" for ADL, "settlement_autoclose-" for settlement.
    /// Empty when it does not fit, which is reported to telemetry.
    /// </summary>
    public ClientOrderId ClientOrderId;

    /// <summary>Order id.</summary>
    public long OrderId;

    /// <summary>Trade id.</summary>
    public long TradeId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Original quantity.</summary>
    public double Quantity;

    /// <summary>Original price. Zero for market orders.</summary>
    public double Price;

    /// <summary>Quantity of this fill.</summary>
    public double LastFilledQuantity;

    /// <summary>Price of this fill.</summary>
    public double LastFilledPrice;

    /// <summary>Side.</summary>
    public OrderSide Side;

    /// <summary>Whether this fill was on the maker side.</summary>
    public bool IsMaker;
}
