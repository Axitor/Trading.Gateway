using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Top bids and asks of one futures symbol, sent whole on every push. Each message replaces the previous one. RPI orders are not included.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct PartialDepthModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Final update id. Grows with every message.</summary>
    public long FinalUpdateId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch.</summary>
    public long TransactionTime;

    /// <summary>Bids, best first. Only the first BidCount are set: read them as model.Bids[..model.BidCount].</summary>
    public DepthLevels20 Bids;

    /// <summary>Asks, best first. Only the first AskCount are set: read them as model.Asks[..model.AskCount].</summary>
    public DepthLevels20 Asks;

    /// <summary>Bids set.</summary>
    public int BidCount;

    /// <summary>Asks set.</summary>
    public int AskCount;
}

