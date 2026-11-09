using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Sbe;

/// <summary>Top 20 bids and asks of a spot symbol from the SBE stream, sent whole on every push.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SbePartialDepthModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Last update id the levels reflect. Grows with every change of the book.</summary>
    public long LastUpdateId;

    /// <summary>Event time, microseconds since the Unix epoch.</summary>
    public long EventTimeMicros;

    /// <summary>Bids, best first. Only the first BidCount are set: read them as model.Bids[..model.BidCount].</summary>
    public DepthLevels20 Bids;

    /// <summary>Asks, best first. Only the first AskCount are set: read them as model.Asks[..model.AskCount].</summary>
    public DepthLevels20 Asks;

    /// <summary>Bids set.</summary>
    public int BidCount;

    /// <summary>Asks set.</summary>
    public int AskCount;
}
