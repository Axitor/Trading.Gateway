namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Order book snapshot of one symbol.</summary>
public struct OrderBookModel
{
    /// <summary>Bids, best first. Empty rather than null when there are none.</summary>
    public DepthLevel[] Bids;

    /// <summary>Asks, best first. Empty rather than null when there are none.</summary>
    public DepthLevel[] Asks;

    /// <summary>Last update id the snapshot reflects. Depth stream updates are applied from it.</summary>
    public long LastUpdateId;

    /// <summary>Message output time, milliseconds since the Unix epoch. Zero on spot.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch. Zero on spot.</summary>
    public long TransactionTime;
}
