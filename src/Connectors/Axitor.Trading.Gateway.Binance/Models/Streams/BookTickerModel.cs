using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Streams;

/// <summary>Best bid and ask of one futures symbol. RPI orders are not included.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from rounding the struct up to 80 bytes.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct BookTickerModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Order book update id. Grows with every update.</summary>
    public long UpdateId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Best bid price.</summary>
    public double BidPrice;

    /// <summary>Best bid quantity. In contracts on COIN-M.</summary>
    public double BidQuantity;

    /// <summary>Best ask price.</summary>
    public double AskPrice;

    /// <summary>Best ask quantity. In contracts on COIN-M.</summary>
    public double AskQuantity;
}
