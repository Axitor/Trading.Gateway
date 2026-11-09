using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Sbe;

/// <summary>One market trade of a spot symbol from the SBE stream. Times are in microseconds.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SbeTradeModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Trade id. Grows with every trade.</summary>
    public long TradeId;

    /// <summary>Event time, microseconds since the Unix epoch.</summary>
    public long EventTimeMicros;

    /// <summary>Trade time, microseconds since the Unix epoch.</summary>
    public long TransactionTimeMicros;

    /// <summary>Price.</summary>
    public double Price;

    /// <summary>Quantity.</summary>
    public double Quantity;

    /// <summary>Whether the buyer was the maker, so the taker sold.</summary>
    public bool IsBuyerMaker;
}
