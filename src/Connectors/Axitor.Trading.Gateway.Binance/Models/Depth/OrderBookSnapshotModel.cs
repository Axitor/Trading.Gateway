namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Order book snapshot read into the caller's buffers: the ids and times, and how many levels each buffer got.</summary>
public struct OrderBookSnapshotModel
{
    /// <summary>Last update id the snapshot reflects. Depth stream updates are applied from it.</summary>
    public long LastUpdateId;

    /// <summary>Message output time, milliseconds since the Unix epoch. Zero on spot.</summary>
    public long EventTime;

    /// <summary>Transaction time, milliseconds since the Unix epoch. Zero on spot.</summary>
    public long TransactionTime;

    /// <summary>Bids written to the bid buffer, best first.</summary>
    public int BidCount;

    /// <summary>Asks written to the ask buffer, best first.</summary>
    public int AskCount;
}
