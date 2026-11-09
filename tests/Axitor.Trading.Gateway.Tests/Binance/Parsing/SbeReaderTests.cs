using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Spot SBE messages and their routing: a broken message reads as false, never throws on the receive loop.</summary>
public sealed class SbeReaderTests
{
    private const long EventTimeMicros = 1700000000123456;
    private const int PriceExponent = -2;
    private const int QuantityExponent = -5;

    [Fact]
    public void ReadsBestBidAsk()
    {
        byte[] frame = BestBidAsk();

        Assert.True(SbeBestBidAskParser.TryParse(frame, out SbeBestBidAskModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(EventTimeMicros, model.EventTimeMicros);
        Assert.Equal(9001, model.UpdateId);
        Assert.Equal(67000.01, model.BidPrice);
        Assert.Equal(1.5, model.BidQuantity);
        Assert.Equal(67000.02, model.AskPrice);
        Assert.Equal(0.25, model.AskQuantity);
        Assert.Equal(model.UpdateId, updateId);
    }

    [Fact]
    public void ReadsEveryTradeOfEvent()
    {
        byte[] frame = Trades();

        Assert.True(SbeTradeParser.TryOpen(frame, out SbeTradeModel model, out int entriesStart, out int entryLength, out int count, out int priceExponent, out int quantityExponent));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(EventTimeMicros, model.EventTimeMicros);
        Assert.Equal(EventTimeMicros - 5, model.TransactionTimeMicros);
        Assert.Equal(2, count);

        SbeTradeParser.ReadTrade(frame, entriesStart, priceExponent, quantityExponent, ref model);
        Assert.Equal(1001, model.TradeId);
        Assert.Equal(67000.01, model.Price);
        Assert.Equal(1.5, model.Quantity);
        Assert.True(model.IsBuyerMaker);

        SbeTradeParser.ReadTrade(frame, entriesStart + entryLength, priceExponent, quantityExponent, ref model);
        Assert.Equal(1002, model.TradeId);
        Assert.Equal(67000.02, model.Price);
        Assert.Equal(0.25, model.Quantity);
        Assert.False(model.IsBuyerMaker);
    }

    [Fact]
    public void ReadsDepthSnapshot()
    {
        byte[] frame = DepthSnapshot(bidCount: 2);

        Assert.True(SbePartialDepthParser.TryParse(frame, out SbePartialDepthModel model, out long updateId));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(EventTimeMicros, model.EventTimeMicros);
        Assert.Equal(7001, model.LastUpdateId);
        Assert.Equal(model.LastUpdateId, updateId);
        Assert.Equal(2, model.BidCount);
        Assert.Equal(Level(67000.01, 1.5), model.Bids[0]);
        Assert.Equal(Level(67000, 2), model.Bids[1]);
        Assert.Equal(1, model.AskCount);
        Assert.Equal(Level(67000.02, 0.25), model.Asks[0]);
    }

    [Fact]
    public void SnapshotWithMoreThanTwentyLevelsIsRejected() =>
        Assert.False(SbePartialDepthParser.TryParse(DepthSnapshot(bidCount: 21), out _, out _));

    [Fact]
    public void ReadsDepthDiffAndGrowsBuffer()
    {
        byte[] frame = DepthDiff();
        DepthLevel[] bids = new DepthLevel[1];
        DepthLevel[] asks = new DepthLevel[1];

        Assert.True(SbeDepthUpdateParser.TryParse(frame, out DepthUpdateHeader header, ref bids, out int bidCount, ref asks, out int askCount));

        Assert.Equal(Symbol.Parse("BTCUSDT"), header.Symbol);
        Assert.Equal(157, header.FirstUpdateId);
        Assert.Equal(160, header.FinalUpdateId);

        // Spot chains by U = previous u + 1, which is the same check against U - 1.
        Assert.Equal(156, header.PreviousFinalUpdateId);

        // The shared depth header keeps milliseconds.
        Assert.Equal(EventTimeMicros / 1000, header.EventTime);

        Assert.Equal(3, bidCount);
        Assert.True(3 <= bids.Length);
        Assert.Equal(Level(67000.01, 1.5), bids[0]);
        Assert.Equal(Level(66999.99, 0), bids[2]);
        Assert.Equal(1, askCount);
        Assert.Equal(Level(67000.02, 0.25), asks[0]);
    }

    [Theory]
    [InlineData(SbeFrameBuilder.TradesTemplateId)]
    [InlineData(SbeFrameBuilder.BestBidAskTemplateId)]
    [InlineData(SbeFrameBuilder.DepthSnapshotTemplateId)]
    [InlineData(SbeFrameBuilder.DepthDiffTemplateId)]
    public void TruncatedFrameIsRejectedAtEveryLength(int templateId)
    {
        byte[] frame = Frame(templateId);

        Assert.True(Parses(templateId, frame));

        for (int length = 0; length < frame.Length; length++)
        {
            ReadOnlySpan<byte> part = frame.AsSpan(0, length);

            bool found = BinanceSbeReader.TryFindSymbol(part, out _, out _, out _);
            bool parsed = Parses(templateId, part);
            WsInboundKind kind = BinanceDialectSbeStream.ReadInbound(part, isBinary: true, out _);

            if (found || parsed || kind != WsInboundKind.Unknown)
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"Template {templateId} cut at {length} of {frame.Length}: found {found}, parsed {parsed}, kind {kind}."));
            }
        }
    }

    [Fact]
    public void ForeignSchemaIsRejected()
    {
        byte[] frame = SbeFrameBuilder.BestBidAsk("BTCUSDT", EventTimeMicros, 9001, PriceExponent, QuantityExponent, 6700001, 150000, 6700002, 25000, schemaId: 2);

        Assert.False(SbeBestBidAskParser.TryParse(frame, out _, out _));
        Assert.Equal(WsInboundKind.Unknown, BinanceDialectSbeStream.ReadInbound(frame, isBinary: true, out _));
    }

    [Fact]
    public void UnknownTemplateIsUnknown()
    {
        byte[] frame = new SbeFrameBuilder().Header(8, 10099).Int64(1).VarString8("BTCUSDT").ToArray();

        Assert.False(BinanceSbeReader.TryFindSymbol(frame, out _, out _, out _));
        Assert.Equal(WsInboundKind.Unknown, BinanceDialectSbeStream.ReadInbound(frame, isBinary: true, out _));
    }

    [Fact]
    public void CountAboveIntMaxIsRejected()
    {
        byte[] frame = new SbeFrameBuilder()
            .Header(SbeFrameBuilder.TradesRootLength, SbeFrameBuilder.TradesTemplateId)
            .Int64(EventTimeMicros).Int64(EventTimeMicros).Int8(PriceExponent).Int8(QuantityExponent)
            .Group(SbeFrameBuilder.TradeEntryLength, 0x8000_0000u)
            .VarString8("BTCUSDT")
            .ToArray();

        Assert.False(SbeTradeParser.TryOpen(frame, out _, out _, out _, out _, out _, out _));
        Assert.False(BinanceSbeReader.TryFindSymbol(frame, out _, out _, out _));
    }

    [Fact]
    public void EntryShorterThanVersionZeroIsRejected()
    {
        byte[] frame = new SbeFrameBuilder()
            .Header(SbeFrameBuilder.TradesRootLength, SbeFrameBuilder.TradesTemplateId)
            .Int64(EventTimeMicros).Int64(EventTimeMicros).Int8(PriceExponent).Int8(QuantityExponent)
            .Group(SbeFrameBuilder.TradeEntryLength - 1, 1)
            .Zeros(SbeFrameBuilder.TradeEntryLength - 1)
            .VarString8("BTCUSDT")
            .ToArray();

        Assert.False(SbeTradeParser.TryOpen(frame, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void LongerBlocksOfNewerVersionStillRead()
    {
        byte[] bestBidAsk = SbeFrameBuilder.BestBidAsk("BTCUSDT", EventTimeMicros, 9001, PriceExponent, QuantityExponent, 6700001, 150000, 6700002, 25000, extraRootBytes: 8);
        byte[] depthDiff = SbeFrameBuilder.DepthDiff("BTCUSDT", EventTimeMicros, 157, 160, PriceExponent, QuantityExponent, [(6700001, 150000)], [(6700002, 25000)], extraEntryBytes: 8);

        Assert.True(SbeBestBidAskParser.TryParse(bestBidAsk, out SbeBestBidAskModel model, out _));
        Assert.Equal(67000.02, model.AskPrice);
        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);

        DepthLevel[] bids = new DepthLevel[4];
        DepthLevel[] asks = new DepthLevel[4];
        Assert.True(SbeDepthUpdateParser.TryParse(depthDiff, out _, ref bids, out _, ref asks, out _));
        Assert.Equal(Level(67000.01, 1.5), bids[0]);
        Assert.Equal(Level(67000.02, 0.25), asks[0]);

        Assert.Equal(WsInboundKind.Push, BinanceDialectSbeStream.ReadInbound(depthDiff, isBinary: true, out WsStreamInbound inbound));
        Assert.Equal("BTCUSDT", Encoding.ASCII.GetString(depthDiff, inbound.KeyStart, inbound.KeyLength));
    }

    [Fact]
    public void DialectRoutesByTemplateAndSymbol()
    {
        byte[] frame = BestBidAsk();

        Assert.Equal(WsInboundKind.Push, BinanceDialectSbeStream.ReadInbound(frame, isBinary: true, out WsStreamInbound inbound));
        Assert.Equal(SbeFrameBuilder.BestBidAskTemplateId, inbound.Channel);
        Assert.Equal("BTCUSDT", Encoding.ASCII.GetString(frame, inbound.KeyStart, inbound.KeyLength));
        Assert.Equal(0, inbound.DataStart);
        Assert.Equal(frame.Length, inbound.DataLength);
    }

    [Fact]
    public void EmptySymbolIsNotRouted()
    {
        byte[] frame = SbeFrameBuilder.BestBidAsk("", EventTimeMicros, 9001, PriceExponent, QuantityExponent, 6700001, 150000, 6700002, 25000);

        Assert.Equal(WsInboundKind.Unknown, BinanceDialectSbeStream.ReadInbound(frame, isBinary: true, out _));
    }

    [Fact]
    public void TextFramesReadAsJson()
    {
        Assert.Equal(WsInboundKind.Response, BinanceDialectSbeStream.ReadInbound("""{"result":null,"id":3}"""u8, isBinary: false, out WsStreamInbound reply));
        Assert.Equal(3, reply.RequestId);

        Assert.Equal(WsInboundKind.Push, BinanceDialectSbeStream.ReadInbound("""{"stream":"!serverShutdown","data":{"e":"serverShutdown","E":1}}"""u8, isBinary: false, out WsStreamInbound shutdown));
        Assert.Equal(WsInboundSignal.ServerShutdown, shutdown.Signal);
    }

    [Fact]
    public void SbeDepthPriceMatchesSnapshotPrice()
    {
        byte[] frame = SbeFrameBuilder.DepthDiff("BTCUSDT", EventTimeMicros, 8, 8, -8, -8, [(6712340000000, 300000000)], []);
        OrderBookModel snapshot = BinanceOrderBookReader.Read("""{"lastUpdateId":7,"bids":[["67123.40","3"]],"asks":[]}"""u8, hasTimes: false);

        DepthLevel[] bids = new DepthLevel[1];
        DepthLevel[] asks = new DepthLevel[1];
        Assert.True(SbeDepthUpdateParser.TryParse(frame, out _, ref bids, out _, ref asks, out _));

        // A spot book synced from a REST snapshot applies SBE updates: one price must be one level.
        Assert.Equal(BitConverter.DoubleToInt64Bits(Assert.Single(snapshot.Bids).Price), BitConverter.DoubleToInt64Bits(bids[0].Price));
    }

    private static byte[] BestBidAsk() =>
        SbeFrameBuilder.BestBidAsk("BTCUSDT", EventTimeMicros, 9001, PriceExponent, QuantityExponent, 6700001, 150000, 6700002, 25000);

    private static byte[] Trades() =>
        SbeFrameBuilder.Trades("BTCUSDT", EventTimeMicros, EventTimeMicros - 5, PriceExponent, QuantityExponent,
            (1001, 6700001, 150000, true),
            (1002, 6700002, 25000, false));

    private static byte[] DepthSnapshot(int bidCount)
    {
        (long Price, long Quantity)[] bids = new (long, long)[bidCount];

        for (int i = 0; i < bids.Length; i++)
        {
            bids[i] = (6700001 - i, i == 0 ? 150000 : 200000);
        }

        return SbeFrameBuilder.DepthSnapshot("BTCUSDT", EventTimeMicros, 7001, PriceExponent, QuantityExponent, bids, [(6700002, 25000)]);
    }

    private static byte[] DepthDiff() =>
        SbeFrameBuilder.DepthDiff("BTCUSDT", EventTimeMicros, 157, 160, PriceExponent, QuantityExponent,
            [(6700001, 150000), (6700000, 200000), (6699999, 0)],
            [(6700002, 25000)]);

    private static byte[] Frame(int templateId) =>
        templateId switch
        {
            SbeFrameBuilder.TradesTemplateId => Trades(),
            SbeFrameBuilder.BestBidAskTemplateId => BestBidAsk(),
            SbeFrameBuilder.DepthSnapshotTemplateId => DepthSnapshot(bidCount: 2),
            _ => DepthDiff(),
        };

    private static bool Parses(int templateId, ReadOnlySpan<byte> data)
    {
        switch (templateId)
        {
            case SbeFrameBuilder.TradesTemplateId:
                return SbeTradeParser.TryOpen(data, out _, out _, out _, out _, out _, out _);

            case SbeFrameBuilder.BestBidAskTemplateId:
                return SbeBestBidAskParser.TryParse(data, out _, out _);

            case SbeFrameBuilder.DepthSnapshotTemplateId:
                return SbePartialDepthParser.TryParse(data, out _, out _);

            default:
                DepthLevel[] bids = new DepthLevel[4];
                DepthLevel[] asks = new DepthLevel[4];
                return SbeDepthUpdateParser.TryParse(data, out _, ref bids, out _, ref asks, out _);
        }
    }

    private static DepthLevel Level(double price, double quantity) => new() { Price = price, Quantity = quantity };
}
