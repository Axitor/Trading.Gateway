using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.KlinesData;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Klines read by position into the caller's buffer.</summary>
public sealed class KlinesParserTests
{
    [Fact]
    public async Task ReadsCandlesOldestFirst()
    {
        Kline[] buffer = new Kline[2];

        int count = await ParseAsync(RestBodies.Klines.ToArray(), buffer);

        Assert.Equal(2, count);

        Kline first = buffer[0];
        Assert.Equal(1499040000000, first.OpenTime);
        Assert.Equal(1499644799999, first.CloseTime);
        Assert.Equal(0.0163479, first.Open);
        Assert.Equal(0.8, first.High);
        Assert.Equal(0.015758, first.Low);
        Assert.Equal(0.015771, first.Close);
        Assert.Equal(148976.11427815, first.Volume);
        Assert.Equal(2434.19055334, first.QuoteVolume);
        Assert.Equal(308, first.TradeCount);
        Assert.Equal(1756.87402397, first.TakerBuyVolume);
        Assert.Equal(28.46694368, first.TakerBuyQuoteVolume);

        Assert.Equal(1499644800000, buffer[1].OpenTime);
        Assert.Equal(42, buffer[1].TradeCount);
        Assert.Equal(7.95, buffer[1].TakerBuyQuoteVolume);
    }

    [Fact]
    public async Task EmptyArrayIsNoCandles()
    {
        Assert.Equal(0, await ParseAsync("[]"u8.ToArray(), new Kline[4]));
    }

    [Fact]
    public async Task FieldsAfterTakerBuyQuoteVolumeAreSkipped()
    {
        byte[] body = """[[1,"1","2","0.5","1.5","10",2,"15",3,"4","6","0","added","9"]]"""u8.ToArray();
        Kline[] buffer = new Kline[1];

        Assert.Equal(1, await ParseAsync(body, buffer));
        Assert.Equal(6, buffer[0].TakerBuyQuoteVolume);
    }

    [Fact]
    public async Task MoreCandlesThanBufferIsMalformed()
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync(RestBodies.Klines.ToArray(), new Kline[1]));
    }

    [Theory]
    [InlineData("""[["1499040000000","0.0163","0.8","0.0157","0.0157","1",1499644799999,"1",1,"1","1","0"]]""")]
    [InlineData("""[[1499040000000,0.0163,"0.8","0.0157","0.0157","1",1499644799999,"1",1,"1","1","0"]]""")]
    [InlineData("""[[1499040000000,"0.0163","0.8"]]""")]
    [InlineData("""{"code":-1121,"msg":"Invalid symbol."}""")]
    public async Task UnexpectedLayoutIsMalformed(string text)
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync(Encoding.ASCII.GetBytes(text), new Kline[4]));
    }

    private static async Task<int> ParseAsync(byte[] body, Kline[] buffer)
    {
        using MemoryStream stream = new(body);
        return await new KlinesDataParser(buffer).ParseAsync(stream, TestContext.Current.CancellationToken);
    }
}
