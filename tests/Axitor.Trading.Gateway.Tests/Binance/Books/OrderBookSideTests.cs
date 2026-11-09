using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Depth;

namespace Axitor.Trading.Gateway.Tests.Binance.Books;

/// <summary>One side of a local book: best first, and only levels it knows exactly.</summary>
public sealed class OrderBookSideTests
{
    [Fact]
    public void KeepsBestFirstOnBothSides()
    {
        OrderBookSide bids = new(8, isAsk: false);
        OrderBookSide asks = new(8, isAsk: true);

        foreach (double price in new[] { 100d, 102d, 101d })
        {
            bids.Apply(price, 1);
            asks.Apply(price, 1);
        }

        Assert.Equal([102d, 101d, 100d], Prices(bids));
        Assert.Equal([100d, 101d, 102d], Prices(asks));
    }

    [Fact]
    public void ZeroRemovesLevelAndNewQuantityReplacesIt()
    {
        OrderBookSide bids = new(8, isAsk: false);
        bids.Apply(100, 1);
        bids.Apply(101, 2);

        bids.Apply(101, 0);
        bids.Apply(100, 5);

        // Removing a level the side does not hold is normal.
        bids.Apply(99, 0);

        Assert.True(bids.TryGetBest(out DepthLevel best));
        Assert.Equal(100, best.Price);
        Assert.Equal(5, best.Quantity);
        Assert.Equal([100d], Prices(bids));
    }

    [Fact]
    public void FullSnapshotIgnoresLevelsBeyondItsDeepest()
    {
        OrderBookSide bids = new(8, isAsk: false);
        OrderBookSide asks = new(8, isAsk: true);

        bids.Load(Levels(100, 99, 98), isFull: true);
        asks.Load(Levels(101, 102, 103), isFull: true);

        // Beyond the deepest snapshot level the book is unknown.
        bids.Apply(97, 5);
        asks.Apply(104, 5);

        // Inside it a new level is known exactly.
        bids.Apply(98.5, 1);
        asks.Apply(102.5, 1);

        Assert.Equal([100d, 99d, 98.5, 98d], Prices(bids));
        Assert.Equal([101d, 102d, 102.5, 103d], Prices(asks));
    }

    [Fact]
    public void ShortSnapshotKnowsTheWholeSide()
    {
        OrderBookSide bids = new(8, isAsk: false);
        bids.Load(Levels(100, 99), isFull: false);

        bids.Apply(50, 1);

        Assert.Equal([100d, 99d, 50d], Prices(bids));
        Assert.False(bids.NeedsRefresh(10));
    }

    [Fact]
    public void FullSideDropsWorstLevelAndMovesBoundary()
    {
        OrderBookSide bids = new(3, isAsk: false);
        bids.Load(Levels(100, 99, 98), isFull: true);

        bids.Apply(101, 1);

        Assert.Equal([101d, 100d, 99d], Prices(bids));

        // 98 fell out, so the side no longer knows anything below 99.
        bids.Apply(98.5, 1);
        Assert.Equal([101d, 100d, 99d], Prices(bids));
    }

    [Fact]
    public void FullSideRefusesWorseLevelAndTightensBoundary()
    {
        OrderBookSide bids = new(3, isAsk: false);
        bids.Load(Levels(100, 99, 98), isFull: true);
        bids.Apply(98, 0);
        bids.Apply(101, 1);

        // Inside the boundary, but worse than every level of a full side.
        bids.Apply(98.5, 1);

        Assert.Equal([101d, 100d, 99d], Prices(bids));

        bids.Apply(98.7, 1);
        Assert.Equal([101d, 100d, 99d], Prices(bids));
    }

    [Fact]
    public void SideWithBoundaryAndNoLevelsIsBlind()
    {
        OrderBookSide bids = new(8, isAsk: false);
        bids.Load(Levels(100, 99), isFull: true);

        Assert.False(bids.IsBlind);
        Assert.True(bids.NeedsRefresh(3));

        bids.Apply(100, 0);
        bids.Apply(99, 0);

        Assert.True(bids.IsBlind);
        Assert.False(bids.TryGetBest(out _));
    }

    [Fact]
    public void ClearedSideIsEmptyNotBlind()
    {
        OrderBookSide asks = new(8, isAsk: true);
        asks.Load(Levels(101), isFull: true);

        asks.Clear();

        Assert.False(asks.IsBlind);
        Assert.Empty(Prices(asks));
    }

    private static DepthLevel[] Levels(params ReadOnlySpan<double> prices)
    {
        DepthLevel[] levels = new DepthLevel[prices.Length];

        for (int i = 0; i < prices.Length; i++)
        {
            levels[i] = new DepthLevel() { Price = prices[i], Quantity = 1 };
        }

        return levels;
    }

    private static double[] Prices(OrderBookSide side)
    {
        DepthLevel[] levels = new DepthLevel[16];
        int count = side.CopyTop(levels);
        double[] prices = new double[count];

        for (int i = 0; i < count; i++)
        {
            prices[i] = levels[i].Price;
        }

        return prices;
    }
}
