using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Order book snapshots of every market, read into new arrays or into the caller's buffers.</summary>
public sealed class OrderBookReaderTests
{
    [Fact]
    public void ReadsFuturesSnapshot()
    {
        OrderBookModel book = BinanceOrderBookReader.Read(RestBodies.FuturesOrderBook, hasTimes: true);

        Assert.Equal(1027024, book.LastUpdateId);
        Assert.Equal(1589436922972, book.EventTime);
        Assert.Equal(1589436922959, book.TransactionTime);
        Assert.Equal(Level(4, 431), Assert.Single(book.Bids));
        Assert.Equal(Level(4.000002, 12), Assert.Single(book.Asks));
    }

    [Fact]
    public void ReadsCoinMSnapshotWithSymbolFirst()
    {
        OrderBookModel book = BinanceOrderBookReader.Read(RestBodies.CoinMOrderBook, hasTimes: true);

        Assert.Equal(16769853, book.LastUpdateId);
        Assert.Equal(1591250106370, book.EventTime);
        Assert.Equal(1591250106368, book.TransactionTime);
        Assert.Equal(Level(9638, 431), Assert.Single(book.Bids));
        Assert.Equal(Level(9638.2, 12), Assert.Single(book.Asks));
    }

    [Fact]
    public void ReadsSpotSnapshotWithoutTimes()
    {
        OrderBookModel book = BinanceOrderBookReader.Read(RestBodies.SpotOrderBook, hasTimes: false);

        Assert.Equal(1027024, book.LastUpdateId);
        Assert.Equal(0, book.EventTime);
        Assert.Equal(0, book.TransactionTime);
        Assert.Equal(Level(4, 431), Assert.Single(book.Bids));
        Assert.Equal(Level(4.000002, 12), Assert.Single(book.Asks));
    }

    [Fact]
    public void WritesIntoCallerBuffersBestFirst()
    {
        byte[] body = """{"lastUpdateId":7,"bids":[["101.5","1"],["101.4","2"]],"asks":[["101.6","3"],["101.7","4"]]}"""u8.ToArray();
        DepthLevel[] bids = new DepthLevel[2];
        DepthLevel[] asks = new DepthLevel[2];

        OrderBookSnapshotModel snapshot = BinanceOrderBookReader.ReadInto(body, hasTimes: false, bids, asks);

        Assert.Equal(7, snapshot.LastUpdateId);
        Assert.Equal(2, snapshot.BidCount);
        Assert.Equal(2, snapshot.AskCount);
        Assert.Equal(Level(101.5, 1), bids[0]);
        Assert.Equal(Level(101.4, 2), bids[1]);
        Assert.Equal(Level(101.6, 3), asks[0]);
        Assert.Equal(Level(101.7, 4), asks[1]);
    }

    [Fact]
    public void MoreLevelsThanBufferIsMalformed()
    {
        byte[] body = """{"lastUpdateId":7,"bids":[["101.5","1"],["101.4","2"]],"asks":[]}"""u8.ToArray();
        DepthLevel[] bids = new DepthLevel[1];
        DepthLevel[] asks = new DepthLevel[1];

        Assert.Throws<JsonException>(() => BinanceOrderBookReader.ReadInto(body, hasTimes: false, bids, asks));
    }

    [Fact]
    public void EmptySideReadsAsNoLevels()
    {
        byte[] body = """{"lastUpdateId":5,"bids":[],"asks":[["1.5","2"]]}"""u8.ToArray();
        DepthLevel[] bids = new DepthLevel[4];
        DepthLevel[] asks = new DepthLevel[4];

        OrderBookSnapshotModel snapshot = BinanceOrderBookReader.ReadInto(body, hasTimes: false, bids, asks);

        Assert.Equal(0, snapshot.BidCount);
        Assert.Equal(1, snapshot.AskCount);
        Assert.Equal(Level(1.5, 2), asks[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("""{"bids":[],"asks":[]}""")]
    [InlineData("""{"lastUpdateId":1,"bids":[["1","2"]""")]
    [InlineData("""{"lastUpdateId":1,"bids":[["1", "2"]],"asks":[]}""")]
    public void MalformedSnapshotThrows(string text)
    {
        byte[] body = Encoding.ASCII.GetBytes(text);

        Assert.Throws<JsonException>(() => BinanceOrderBookReader.Read(body, hasTimes: false));
    }

    [Fact]
    public void SamePriceIsSameLevelInSnapshotAndStream()
    {
        byte[] snapshotBody = """{"lastUpdateId":7,"E":1,"T":1,"bids":[["67123.40","3"]],"asks":[]}"""u8.ToArray();
        byte[] update = """{"e":"depthUpdate","E":2,"T":2,"s":"BTCUSDT","ps":"BTCUSDT","U":8,"u":8,"pu":7,"b":[["67123.40","1.5"]],"a":[]}"""u8.ToArray();

        OrderBookModel snapshot = BinanceOrderBookReader.Read(snapshotBody, hasTimes: true);

        DepthLevel[] bids = new DepthLevel[1];
        DepthLevel[] asks = new DepthLevel[1];
        Assert.True(FuturesDepthUpdateParser.TryParse(update, out _, ref bids, out int bidCount, ref asks, out _));

        // The local book keys levels by price: two doubles for one price would split the level in two.
        Assert.Equal(1, bidCount);
        Assert.Equal(BitConverter.DoubleToInt64Bits(Assert.Single(snapshot.Bids).Price), BitConverter.DoubleToInt64Bits(bids[0].Price));
    }

    private static DepthLevel Level(double price, double quantity) => new() { Price = price, Quantity = quantity };
}
