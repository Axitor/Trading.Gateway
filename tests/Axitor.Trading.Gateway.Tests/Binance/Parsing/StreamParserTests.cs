using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Every field of every stream parser, and how the reader copes with a changed format.</summary>
public sealed class StreamParserTests
{
    [Fact]
    public void ReadsFuturesBookTicker()
    {
        Assert.True(FuturesBookTickerParser.TryParse(FuturesFrames.BookTicker, out BookTickerModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BNBUSDT"), model.Symbol);
        Assert.Equal(400900217, model.UpdateId);
        Assert.Equal(25.3519, model.BidPrice);
        Assert.Equal(31.21, model.BidQuantity);
        Assert.Equal(25.3652, model.AskPrice);
        Assert.Equal(40.66, model.AskQuantity);
        Assert.Equal(1568014460891, model.TransactionTime);
        Assert.Equal(1568014460893, model.EventTime);
        Assert.Equal(model.UpdateId, updateId);
    }

    [Fact]
    public void ReadsFuturesAggTradeWithAndWithoutNq()
    {
        Assert.True(FuturesAggTradeParser.TryParse(FuturesFrames.AggTrade, out AggTradeModel usdM, out long updateId));
        Assert.True(FuturesAggTradeParser.TryParse(FuturesFrames.AggTradeCoinM, out AggTradeModel coinM, out _));

        Assert.Equal(Symbol.Parse("BTCUSDT"), usdM.Symbol);
        Assert.Equal(5933014, usdM.AggregateTradeId);
        Assert.Equal(123456789, usdM.EventTime);
        Assert.Equal(123456785, usdM.TradeTime);
        Assert.Equal(100, usdM.FirstTradeId);
        Assert.Equal(105, usdM.LastTradeId);
        Assert.Equal(0.001, usdM.Price);
        Assert.Equal(100, usdM.Quantity);
        Assert.True(usdM.IsBuyerMaker);
        Assert.Equal(usdM.AggregateTradeId, updateId);

        Assert.Equal(Symbol.Parse("BTCUSD_PERP"), coinM.Symbol);
        Assert.Equal(usdM.LastTradeId, coinM.LastTradeId);
        Assert.Equal(usdM.TradeTime, coinM.TradeTime);
    }

    [Fact]
    public void ReadsFuturesDepthAndGrowsLevelBuffer()
    {
        DepthLevel[] bids = new DepthLevel[1];
        DepthLevel[] asks = new DepthLevel[1];

        Assert.True(FuturesDepthUpdateParser.TryParse(FuturesFrames.DepthUpdate, out DepthUpdateHeader header, ref bids, out int bidCount, ref asks, out int askCount));

        Assert.Equal(Symbol.Parse("BTCUSDT"), header.Symbol);
        Assert.Equal(123456789, header.EventTime);
        Assert.Equal(123456788, header.TransactionTime);
        Assert.Equal(157, header.FirstUpdateId);
        Assert.Equal(160, header.FinalUpdateId);
        Assert.Equal(149, header.PreviousFinalUpdateId);

        Assert.Equal(1, bidCount);
        Assert.Equal(Level(0.0024, 10), bids[0]);

        // Two asks did not fit the buffer of one: it was replaced by a larger one.
        Assert.Equal(2, askCount);
        Assert.True(2 <= asks.Length);
        Assert.Equal(Level(0.0026, 100), asks[0]);
        Assert.Equal(Level(0.0027, 0), asks[1]);
    }

    [Fact]
    public void ReadsFuturesPartialDepth()
    {
        Assert.True(FuturesPartialDepthParser.TryParse(FuturesFrames.PartialDepth, out PartialDepthModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(1571889248277, model.EventTime);
        Assert.Equal(1571889248276, model.TransactionTime);
        Assert.Equal(390497878, model.FinalUpdateId);
        Assert.Equal(model.FinalUpdateId, updateId);

        Assert.Equal(2, model.BidCount);
        Assert.Equal(Level(7403.89, 0.002), model.Bids[0]);
        Assert.Equal(Level(7403.9, 3.906), model.Bids[1]);

        Assert.Equal(2, model.AskCount);
        Assert.Equal(Level(7405.96, 3.34), model.Asks[0]);
        Assert.Equal(Level(7406.63, 4.525), model.Asks[1]);
    }

    [Fact]
    public void ReadsFuturesKline()
    {
        Assert.True(FuturesKlineParser.TryParse(FuturesFrames.Kline, out KlineUpdateModel model, out _));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(1638747660000, model.EventTime);
        Assert.Equal(100, model.FirstTradeId);
        Assert.Equal(200, model.LastTradeId);
        Assert.False(model.IsClosed);

        Assert.Equal(1638747660000, model.Kline.OpenTime);
        Assert.Equal(1638747719999, model.Kline.CloseTime);
        Assert.Equal(0.001, model.Kline.Open);
        Assert.Equal(0.002, model.Kline.Close);
        Assert.Equal(0.0025, model.Kline.High);
        Assert.Equal(0.0015, model.Kline.Low);
        Assert.Equal(1000, model.Kline.Volume);
        Assert.Equal(100, model.Kline.TradeCount);
        Assert.Equal(1, model.Kline.QuoteVolume);
        Assert.Equal(500, model.Kline.TakerBuyVolume);
        Assert.Equal(0.5, model.Kline.TakerBuyQuoteVolume);
    }

    [Fact]
    public void ClosingKlineWithSameEventTimeIsNotDuplicate()
    {
        Assert.True(FuturesKlineParser.TryParse(FuturesFrames.Kline, out _, out long openId));
        Assert.True(FuturesKlineParser.TryParse(FuturesFrames.KlineClosed, out KlineUpdateModel closed, out long closedId));

        Assert.True(closed.IsClosed);
        Assert.Equal(1638747660000L << 1, openId);
        Assert.Equal((1638747660000L << 1) | 1, closedId);

        // The dispatcher drops ids that do not grow: the final candle must pass.
        Assert.True(openId < closedId);
    }

    [Fact]
    public void ReadsSpotBookTicker()
    {
        Assert.True(SpotBookTickerParser.TryParse(SpotFrames.BookTicker, out SpotBookTickerModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BNBUSDT"), model.Symbol);
        Assert.Equal(400900217, model.UpdateId);
        Assert.Equal(25.3519, model.BidPrice);
        Assert.Equal(31.21, model.BidQuantity);
        Assert.Equal(25.3652, model.AskPrice);
        Assert.Equal(40.66, model.AskQuantity);
        Assert.Equal(model.UpdateId, updateId);
    }

    [Fact]
    public void ReadsSpotAggTrade()
    {
        Assert.True(SpotAggTradeParser.TryParse(SpotFrames.AggTrade, out AggTradeModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BNBBTC"), model.Symbol);
        Assert.Equal(12345, model.AggregateTradeId);
        Assert.Equal(1672515782136, model.EventTime);
        Assert.Equal(1672515782136, model.TradeTime);
        Assert.Equal(100, model.FirstTradeId);
        Assert.Equal(105, model.LastTradeId);
        Assert.Equal(0.001, model.Price);
        Assert.Equal(100, model.Quantity);
        Assert.True(model.IsBuyerMaker);
        Assert.Equal(model.AggregateTradeId, updateId);
    }

    [Fact]
    public void SpotDepthChainsByFirstIdMinusOne()
    {
        DepthLevel[] bids = new DepthLevel[4];
        DepthLevel[] asks = new DepthLevel[4];

        Assert.True(SpotDepthUpdateParser.TryParse(SpotFrames.DepthUpdate, out DepthUpdateHeader header, ref bids, out int bidCount, ref asks, out int askCount));

        Assert.Equal(Symbol.Parse("BNBBTC"), header.Symbol);
        Assert.Equal(1672515782136, header.EventTime);
        Assert.Equal(0, header.TransactionTime);
        Assert.Equal(157, header.FirstUpdateId);
        Assert.Equal(160, header.FinalUpdateId);

        // Spot sends no pu: the chain check against U - 1 is the same as U = previous u + 1.
        Assert.Equal(156, header.PreviousFinalUpdateId);

        Assert.Equal(1, bidCount);
        Assert.Equal(Level(0.0024, 10), bids[0]);
        Assert.Equal(1, askCount);
        Assert.Equal(Level(0.0026, 100), asks[0]);
    }

    [Fact]
    public void ReadsSpotPartialDepth()
    {
        Assert.True(SpotPartialDepthParser.TryParse(SpotFrames.PartialDepth, out SpotPartialDepthModel model, out long updateId));

        Assert.Equal(160, model.LastUpdateId);
        Assert.Equal(model.LastUpdateId, updateId);
        Assert.Equal(1, model.BidCount);
        Assert.Equal(Level(0.0024, 10), model.Bids[0]);
        Assert.Equal(1, model.AskCount);
        Assert.Equal(Level(0.0026, 100), model.Asks[0]);
    }

    [Fact]
    public void FieldAddedByExchangeIsSkipped()
    {
        byte[] withExtra = Encoding.ASCII.GetBytes(
            """{"e":"bookTicker","u":400900217,"s":"BNBUSDT","ps":"BNBUSDT","rpi":"1","b":"25.35190000","B":"31.21000000","a":"25.36520000","A":"40.66000000","T":1568014460891,"E":1568014460893,"st":0}""");

        Assert.True(FuturesBookTickerParser.TryParse(FuturesFrames.BookTicker, out BookTickerModel expected, out _));
        Assert.True(FuturesBookTickerParser.TryParse(withExtra, out BookTickerModel actual, out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KeysInAnotherOrderStillParse()
    {
        byte[] reordered = Encoding.ASCII.GetBytes(
            """{"e":"bookTicker","E":1568014460893,"T":1568014460891,"A":"40.66000000","a":"25.36520000","B":"31.21000000","b":"25.35190000","ps":"BNBUSDT","s":"BNBUSDT","u":400900217}""");

        Assert.True(FuturesBookTickerParser.TryParse(FuturesFrames.BookTicker, out BookTickerModel expected, out _));
        Assert.True(FuturesBookTickerParser.TryParse(reordered, out BookTickerModel actual, out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MissingFieldIsUnreadable()
    {
        byte[] noBid = Encoding.ASCII.GetBytes(
            """{"e":"bookTicker","u":400900217,"s":"BNBUSDT","ps":"BNBUSDT","B":"31.21000000","a":"25.36520000","A":"40.66000000","T":1568014460891,"E":1568014460893}""");

        Assert.False(FuturesBookTickerParser.TryParse(noBid, out _, out _));
    }

    private static DepthLevel Level(double price, double quantity) => new() { Price = price, Quantity = quantity };
}
