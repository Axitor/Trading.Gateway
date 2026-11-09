namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Top bids and asks of one spot symbol, sent whole on every push. Spot sends no symbol: the subscription handle tells which one.</summary>
public struct SpotPartialDepthModel
{
    /// <summary>Last update id the levels reflect. Grows with every change of the book.</summary>
    public long LastUpdateId;

    /// <summary>Bids, best first. Only the first BidCount are set: read them as model.Bids[..model.BidCount].</summary>
    public DepthLevels20 Bids;

    /// <summary>Asks, best first. Only the first AskCount are set: read them as model.Asks[..model.AskCount].</summary>
    public DepthLevels20 Asks;

    /// <summary>Bids set.</summary>
    public int BidCount;

    /// <summary>Asks set.</summary>
    public int AskCount;
}
