using System.Runtime.InteropServices;

namespace Axitor.Trading.Gateway.Binance.Models.Klines;

/// <summary>
/// Klines/Candlestick Data Response Model.
/// </summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Fields ordered for Mechanical Sympathy (memory alignment and cache density), ignoring API JSON order.
/// </para>
/// <para>
/// DESIGN NOTE: In low-latency algo trading, prices are parsed as fast double primitives rather than slow decimal types to maximize performance.
/// The theoretical precision loss of floating-point arithmetic is irrelevant here, as we do not perform the deep cumulative operations required to manifest it. 
/// Furthermore, all outbound order prices and quantities are strictly rounded to the exchange's tick and step sizes prior to transmission, rendering decimal precision unnecessary.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct Kline
{
    /// <summary>Open time, milliseconds since the Unix epoch. Uniquely identifies the candle.</summary>
    public long OpenTime;

    /// <summary>Close time, milliseconds since the Unix epoch.</summary>
    public long CloseTime;

    /// <summary>Open price.</summary>
    public double Open;

    /// <summary>Highest price.</summary>
    public double High;

    /// <summary>Lowest price.</summary>
    public double Low;

    /// <summary>Close price, or the latest price while the candle is still open.</summary>
    public double Close;

    /// <summary>Volume in the base asset.</summary>
    public double Volume;

    /// <summary>Volume in the quote asset.</summary>
    public double QuoteVolume;

    /// <summary>Taker buy volume in the base asset.</summary>
    public double TakerBuyVolume;

    /// <summary>Taker buy volume in the quote asset.</summary>
    public double TakerBuyQuoteVolume;

    /// <summary>Number of trades in the candle.</summary>
    public int TradeCount;

}
