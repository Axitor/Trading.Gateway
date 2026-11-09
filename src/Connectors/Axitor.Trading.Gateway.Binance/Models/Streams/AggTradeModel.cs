using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Streams;

/// <summary>Market trades of one taker order at one price. Insurance fund and ADL trades are not aggregated.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from rounding the struct up to 96 bytes.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AggTradeModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Aggregate trade id. Grows with every aggregate trade.</summary>
    public long AggregateTradeId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Trade time, milliseconds since the Unix epoch.</summary>
    public long TradeTime;

    /// <summary>First trade id in the aggregate.</summary>
    public long FirstTradeId;

    /// <summary>Last trade id in the aggregate.</summary>
    public long LastTradeId;

    /// <summary>Price.</summary>
    public double Price;

    /// <summary>Quantity. In contracts on COIN-M.</summary>
    public double Quantity;

    /// <summary>Whether the buyer was the maker, so the taker sold.</summary>
    public bool IsBuyerMaker;
}
