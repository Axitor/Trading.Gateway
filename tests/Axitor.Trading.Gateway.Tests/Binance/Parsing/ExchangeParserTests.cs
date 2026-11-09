using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Trading rules from exchangeInfo: a rule silently read as zero would turn it off for every order.</summary>
public sealed class ExchangeParserTests
{
    // MIN_NOTIONAL writes its value before its type, as the live API does.
    private const string Filters =
        """[{"filterType":"PRICE_FILTER","maxPrice":"300","minPrice":"0.0001","tickSize":"0.0001"},{"filterType":"LOT_SIZE","maxQty":"10000000","minQty":"1","stepSize":"1"},{"filterType":"MARKET_LOT_SIZE","maxQty":"590119","minQty":"2","stepSize":"2"},{"filterType":"MAX_NUM_ORDERS","limit":200},{"notional":"5.0","filterType":"MIN_NOTIONAL"},{"filterType":"PERCENT_PRICE","multiplierUp":"1.1500","multiplierDown":"0.8500","multiplierDecimal":"4"}]""";

    [Fact]
    public async Task ReadsFiltersOrdersAreCheckedBy()
    {
        ExchangeInfoModel model = await ParseAsync(Body(Symbol("BLZUSDT", "TRADING", Filters)));

        ExchangeSymbol symbol = Assert.Single(model.Symbols);
        SymbolFilters filters = symbol.Filters;

        Assert.Equal(Axitor.Trading.Gateway.Core.Models.Primitives.Symbol.Parse("BLZUSDT"), symbol.Symbol);
        Assert.Equal(SymbolStatus.TRADING, symbol.Status);
        Assert.Equal(0.0001, filters.MinPrice);
        Assert.Equal(300, filters.MaxPrice);
        Assert.Equal(0.0001, filters.TickSize);
        Assert.Equal(1, filters.MinQuantity);
        Assert.Equal(10000000, filters.MaxQuantity);
        Assert.Equal(1, filters.StepSize);
        Assert.Equal(2, filters.MarketMinQuantity);
        Assert.Equal(590119, filters.MarketMaxQuantity);
        Assert.Equal(2, filters.MarketStepSize);
        Assert.Equal(5, filters.MinNotional);
    }

    [Fact]
    public async Task UnreadableFilterValueIsMalformed()
    {
        string filters = """[{"filterType":"PRICE_FILTER","maxPrice":"300","minPrice":"0.0001","tickSize":"abc"}]""";

        await Assert.ThrowsAsync<JsonException>(() => ParseAsync(Body(Symbol("BLZUSDT", "TRADING", filters))));
    }

    [Fact]
    public async Task UnknownStatusReadsAsUnknown()
    {
        ExchangeInfoModel model = await ParseAsync(Body(Symbol("BLZUSDT", "NEW_STATUS", "[]")));

        Assert.Equal(SymbolStatus.UNKNOWN, Assert.Single(model.Symbols).Status);
    }

    [Fact]
    public async Task SymbolThatDoesNotFitIsLeftOut()
    {
        ExchangeInfoModel model = await ParseAsync(Body(Symbol("ABCDEFGHIJKLMNOPQ", "TRADING", "[]") + "," + Symbol("BLZUSDT", "TRADING", "[]")));

        Assert.Equal(Axitor.Trading.Gateway.Core.Models.Primitives.Symbol.Parse("BLZUSDT"), Assert.Single(model.Symbols).Symbol);
    }

    [Fact]
    public async Task AnswerWithoutSymbolsIsMalformed()
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync("""{"timezone":"UTC","serverTime":1565246363776}"""));
    }

    private static string Body(string symbols) =>
        """{"timezone":"UTC","serverTime":1565246363776,"rateLimits":[],"exchangeFilters":[],"assets":[],"symbols":[""" + symbols + "]}";

    private static string Symbol(string name, string status, string filters) =>
        $$"""{"symbol":"{{name}}","pair":"{{name}}","contractType":"PERPETUAL","status":"{{status}}","baseAsset":"BLZ","quoteAsset":"USDT","filters":{{filters}},"orderTypes":["LIMIT"],"timeInForce":["GTC"]}""";

    private static async Task<ExchangeInfoModel> ParseAsync(string text)
    {
        using MemoryStream body = new(Encoding.ASCII.GetBytes(text));
        return await default(ExchangeInfoParser).ParseAsync(body, TestContext.Current.CancellationToken);
    }
}
