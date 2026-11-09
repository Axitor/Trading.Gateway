using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Sbe;

/// <summary>Best bid and ask of a spot symbol from the SBE stream. Under high load the exchange drops stale updates and sends only the latest.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SbeBestBidAskModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Order book update id. Grows with every update.</summary>
    public long UpdateId;

    /// <summary>Event time, microseconds since the Unix epoch.</summary>
    public long EventTimeMicros;

    /// <summary>Best bid price.</summary>
    public double BidPrice;

    /// <summary>Best bid quantity.</summary>
    public double BidQuantity;

    /// <summary>Best ask price.</summary>
    public double AskPrice;

    /// <summary>Best ask quantity.</summary>
    public double AskQuantity;
}
