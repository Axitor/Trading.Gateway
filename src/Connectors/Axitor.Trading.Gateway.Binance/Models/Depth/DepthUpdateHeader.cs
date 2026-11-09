using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Order book changes of one symbol: depthUpdate. Its bid and ask levels come as spans in the same call.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct DepthUpdateHeader
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>First update id in the event.</summary>
    public long FirstUpdateId;

    /// <summary>Final update id in the event. Grows with every event.</summary>
    public long FinalUpdateId;

    /// <summary>
    /// Final update id of the previous event. When it differs from the last one applied, updates were missed and a local order book must be rebuilt.
    /// Spot does not send it; there it is set to FirstUpdateId - 1.
    /// </summary>
    public long PreviousFinalUpdateId;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch. Zero on spot.</summary>
    public long TransactionTime;
}
