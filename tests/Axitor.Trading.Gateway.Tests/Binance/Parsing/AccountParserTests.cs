using System.Globalization;
using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Parsing.Account;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Account answers: a margin figure or leverage read wrong would mislead risk checks, so unreadable values throw.</summary>
public sealed class AccountParserTests
{
    private const string AccountV3 =
        """{"totalInitialMargin":"1.5","totalMaintMargin":"0.5","totalWalletBalance":"103.12345678","totalUnrealizedProfit":"-2.25","totalMarginBalance":"100.87345678","totalPositionInitialMargin":"1.25","totalOpenOrderInitialMargin":"0.25","totalCrossWalletBalance":"103.12345678","totalCrossUnPnl":"-2.25","availableBalance":"99.37345678","maxWithdrawAmount":"99.37345678","assets":[{"asset":"USDT","walletBalance":"23.72469206","unrealizedProfit":"-2.25","marginBalance":"21.47469206","maintMargin":"0.5","initialMargin":"1.5","positionInitialMargin":"1.25","openOrderInitialMargin":"0.25","crossWalletBalance":"23.72469206","crossUnPnl":"-2.25","availableBalance":"19.97469206","maxWithdrawAmount":"19.97469206","updateTime":1625474304765}],"positions":[{"symbol":"BTCUSDT","positionSide":"BOTH","positionAmt":"-0.010","unrealizedProfit":"-2.25","isolatedMargin":"0","notional":"-602.25","isolatedWallet":"0","initialMargin":"1.25","maintMargin":"0.5","updateTime":1625474304765}]}""";

    private const string AccountV2Rest =
        """{"feeTier":1,"feeBurn":true,"canTrade":true,"canDeposit":true,"canWithdraw":false,"updateTime":0,"multiAssetsMargin":false,"tradeGroupId":-1,"totalInitialMargin":"0","totalMaintMargin":"0","totalWalletBalance":"23.72469206","totalUnrealizedProfit":"0","totalMarginBalance":"23.72469206","totalPositionInitialMargin":"0","totalOpenOrderInitialMargin":"0","totalCrossWalletBalance":"23.72469206","totalCrossUnPnl":"0","availableBalance":"23.72469206","maxWithdrawAmount":"23.72469206","assets":[{"asset":"USDT","walletBalance":"23.72469206","unrealizedProfit":"0","marginBalance":"23.72469206","maintMargin":"0","initialMargin":"0","positionInitialMargin":"0","openOrderInitialMargin":"0","crossWalletBalance":"23.72469206","crossUnPnl":"0","availableBalance":"23.72469206","maxWithdrawAmount":"23.72469206","marginAvailable":true,"updateTime":1625474304765}],"positions":[{"symbol":"BTCUSDT","initialMargin":"0","maintMargin":"0","unrealizedProfit":"0","positionInitialMargin":"0","openOrderInitialMargin":"0","leverage":"100","isolated":true,"entryPrice":"0.00000","maxNotional":"250000","bidNotional":"0","askNotional":"0","positionSide":"BOTH","positionAmt":"0","updateTime":0}]}""";

    private const string AccountV2WsApi =
        """{"feeTier":1,"canTrade":true,"canDeposit":true,"canWithdraw":true,"updateTime":0,"multiAssetsMargin":true,"tradeGroupId":-1,"totalWalletBalance":"10","assets":[],"positions":[{"symbol":"ETHUSDT","initialMargin":"5","maintMargin":"1","unrealizedProfit":"2","positionInitialMargin":"5","openOrderInitialMargin":"0","leverage":"20","isolated":false,"entryPrice":"3000.5","breakEvenPrice":"3001.25","maxNotional":"500000","positionSide":"LONG","positionAmt":"0.1","updateTime":1720000000000}]}""";

    [Fact]
    public async Task ReadsAccountV3()
    {
        AccountInfoModel model = await ParseAsync<AccountInfoParser, AccountInfoModel>(AccountV3);

        Assert.Equal(103.12345678, model.TotalWalletBalance);
        Assert.Equal(-2.25, model.TotalUnrealizedProfit);
        Assert.Equal(1.5, model.TotalInitialMargin);
        Assert.Equal(0.5, model.TotalMaintenanceMargin);
        Assert.Equal(99.37345678, model.AvailableBalance);

        AssetBalance asset = Assert.Single(model.Assets);
        Assert.Equal(Symbol.Parse("USDT"), asset.Asset);
        Assert.Equal(23.72469206, asset.WalletBalance);
        Assert.Equal(19.97469206, asset.AvailableBalance);
        Assert.Equal(1625474304765, asset.UpdateTime);

        AccountPosition position = Assert.Single(model.Positions);
        Assert.Equal(Symbol.Parse("BTCUSDT"), position.Symbol);
        Assert.Equal(PositionSide.BOTH, position.Side);
        Assert.Equal(-0.01, position.Quantity);
        Assert.Equal(-602.25, position.Notional);
        Assert.Equal(0.5, position.MaintenanceMargin);
    }

    [Fact]
    public async Task AccountWithNothingOpenHasEmptyArrays()
    {
        AccountInfoModel v3 = await ParseAsync<AccountInfoParser, AccountInfoModel>("""{"totalWalletBalance":"1"}""");
        AccountInfoV2Model v2 = AccountInfoV2Parser.Parse("""{"totalWalletBalance":"1","assets":null}"""u8);

        Assert.Empty(v3.Assets);
        Assert.Empty(v3.Positions);
        Assert.Empty(v2.Assets);
        Assert.Empty(v2.Positions);
    }

    [Theory]
    [InlineData("""{"totalWalletBalance":"abc"}""")]
    [InlineData("""{"assets":[{"asset":"USDT","walletBalance":"abc"}]}""")]
    [InlineData("""{"positions":[{"symbol":"BTCUSDT","positionSide":"MIDDLE"}]}""")]
    [InlineData("""{"positions":{"symbol":"BTCUSDT"}}""")]
    public async Task UnreadableAccountValueIsMalformed(string text)
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync<AccountInfoParser, AccountInfoModel>(text));
        Assert.Throws<JsonException>(() => AccountInfoV2Parser.Parse(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public async Task ReadsPositionsPastStartingBuffer()
    {
        // The pooled buffer starts at 64 positions.
        AccountInfoModel model = await ParseAsync<AccountInfoParser, AccountInfoModel>(PositionsV3(70));

        Assert.Equal(70, model.Positions.Length);
        Assert.Equal(Symbol.Parse("S69USDT"), model.Positions[69].Symbol);
        Assert.Equal(69, model.Positions[69].UpdateTime);
    }

    [Fact]
    public void ReadsRestAccountV2()
    {
        AccountInfoV2Model model = AccountInfoV2Parser.Parse(Encoding.ASCII.GetBytes(AccountV2Rest));

        Assert.Equal(1, model.FeeTier);
        Assert.True(model.FeeBurn);
        Assert.True(model.CanTrade);
        Assert.False(model.CanWithdraw);
        Assert.False(model.IsMultiAssetsMargin);
        Assert.Equal(-1, model.TradeGroupId);
        Assert.Equal(23.72469206, model.TotalWalletBalance);
        Assert.True(Assert.Single(model.Assets).MarginAvailable);

        AccountInfoV2Position position = Assert.Single(model.Positions);
        Assert.Equal(Symbol.Parse("BTCUSDT"), position.Symbol);
        Assert.Equal(100, position.Leverage);
        Assert.True(position.IsIsolated);
        Assert.Equal(250000, position.MaxNotional);

        // REST does not send it.
        Assert.Equal(0, position.BreakEvenPrice);
    }

    [Fact]
    public void ReadsWsApiAccountV2()
    {
        AccountInfoV2Model model = AccountInfoV2Parser.Parse(Encoding.ASCII.GetBytes(AccountV2WsApi));

        // The WebSocket API does not send it.
        Assert.False(model.FeeBurn);
        Assert.True(model.IsMultiAssetsMargin);
        Assert.Empty(model.Assets);

        AccountInfoV2Position position = Assert.Single(model.Positions);
        Assert.Equal(Symbol.Parse("ETHUSDT"), position.Symbol);
        Assert.Equal(PositionSide.LONG, position.Side);
        Assert.Equal(20, position.Leverage);
        Assert.False(position.IsIsolated);
        Assert.Equal(3000.5, position.EntryPrice);
        Assert.Equal(3001.25, position.BreakEvenPrice);
        Assert.Equal(0.1, position.Quantity);
        Assert.Equal(1720000000000, position.UpdateTime);
    }

    [Fact]
    public void ReadsEverySymbolPastStartingBuffer()
    {
        // V2 lists every symbol of the market; the pooled buffer starts at 1024.
        StringBuilder body = new("""{"positions":[""");

        for (int i = 0; i < 1100; i++)
        {
            body.Append(i == 0 ? "" : ",");
            body.Append(CultureInfo.InvariantCulture, $$"""{"symbol":"S{{i}}USDT","leverage":"{{i % 125 + 1}}","positionSide":"BOTH","positionAmt":"0"}""");
        }

        body.Append("]}");

        AccountInfoV2Model model = AccountInfoV2Parser.Parse(Encoding.ASCII.GetBytes(body.ToString()));

        Assert.Equal(1100, model.Positions.Length);
        Assert.Equal(Symbol.Parse("S1099USDT"), model.Positions[1099].Symbol);
        Assert.Equal(1099 % 125 + 1, model.Positions[1099].Leverage);
    }

    [Fact]
    public async Task ReadsBracketsFromArrayOrObjectAlike()
    {
        const string symbol = """{"symbol":"ETHUSDT","notionalCoef":1.50,"brackets":[{"bracket":1,"initialLeverage":75,"notionalCap":10000,"notionalFloor":0,"maintMarginRatio":0.0065,"cum":0},{"bracket":2,"initialLeverage":50,"notionalCap":50000,"notionalFloor":10000,"maintMarginRatio":0.01,"cum":35}]}""";

        LeverageBracketModel fromArray = await ParseAsync<LeverageBracketParser, LeverageBracketModel>("[" + symbol + "]");
        LeverageBracketModel fromObject = await ParseAsync<LeverageBracketParser, LeverageBracketModel>(symbol);

        foreach (LeverageBracketModel model in new[] { fromArray, fromObject })
        {
            SymbolBrackets brackets = Assert.Single(model.Symbols);
            Assert.Equal(Symbol.Parse("ETHUSDT"), brackets.Symbol);
            Assert.Equal(1.5, brackets.NotionalCoef);
            Assert.Equal(2, brackets.Brackets.Length);

            LeverageBracketLevel second = brackets.Brackets[1];
            Assert.Equal(2, second.Bracket);
            Assert.Equal(50, second.InitialLeverage);
            Assert.Equal(10000, second.NotionalFloor);
            Assert.Equal(50000, second.NotionalCap);
            Assert.Equal(0.01, second.MaintMarginRatio);
            Assert.Equal(35, second.Cum);
        }
    }

    [Fact]
    public async Task MissingNotionalCoefIsOne()
    {
        LeverageBracketModel model = await ParseAsync<LeverageBracketParser, LeverageBracketModel>(
            """[{"symbol":"ETHUSDT","brackets":[]}]""");

        Assert.Equal(1, Assert.Single(model.Symbols).NotionalCoef);
    }

    [Theory]
    [InlineData("""[{"symbol":"ETHUSDT"}]""")]
    [InlineData("""[{"brackets":[]}]""")]
    [InlineData("""[{"symbol":"ETHUSDT","brackets":[{"bracket":1,"initialLeverage":75,"notionalCap":10000,"notionalFloor":0,"maintMarginRatio":0.0065}]}]""")]
    [InlineData("""[{"symbol":"ETHUSDT","notionalCoef":"x","brackets":[]}]""")]
    public async Task IncompleteBracketsAreMalformed(string text)
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync<LeverageBracketParser, LeverageBracketModel>(text));
    }

    [Fact]
    public async Task ReadsBracketsPastStartingBuffers()
    {
        // Pooled buffers start at 1024 symbols and 32 levels.
        StringBuilder body = new("[");

        for (int i = 0; i < 1500; i++)
        {
            body.Append(i == 0 ? "" : ",");
            body.Append(CultureInfo.InvariantCulture, $$"""{"symbol":"S{{i}}USDT","brackets":[""");

            int levels = i == 1499 ? 40 : 1;

            for (int level = 1; level <= levels; level++)
            {
                body.Append(level == 1 ? "" : ",");
                body.Append(CultureInfo.InvariantCulture, $$"""{"bracket":{{level}},"initialLeverage":{{126 - level}},"notionalCap":{{level * 1000}},"notionalFloor":{{(level - 1) * 1000}},"maintMarginRatio":0.01,"cum":0}""");
            }

            body.Append("]}");
        }

        body.Append(']');

        LeverageBracketModel model = await ParseAsync<LeverageBracketParser, LeverageBracketModel>(body.ToString());

        Assert.Equal(1500, model.Symbols.Length);
        Assert.Single(model.Symbols[0].Brackets);

        SymbolBrackets last = model.Symbols[1499];
        Assert.Equal(Symbol.Parse("S1499USDT"), last.Symbol);
        Assert.Equal(40, last.Brackets.Length);
        Assert.Equal(40000, last.Brackets[39].NotionalCap);
    }

    [Fact]
    public async Task BracketSymbolThatDoesNotFitIsLeftOut()
    {
        LeverageBracketModel model = await ParseAsync<LeverageBracketParser, LeverageBracketModel>(
            """[{"symbol":"ABCDEFGHIJKLMNOPQ","brackets":[]},{"symbol":"ETHUSDT","brackets":[]}]""");

        Assert.Equal(Symbol.Parse("ETHUSDT"), Assert.Single(model.Symbols).Symbol);
    }

    [Fact]
    public async Task ReadsSymbolConfig()
    {
        SymbolConfigModel model = await ParseAsync<SymbolConfigParser, SymbolConfigModel>(
            """[{"symbol":"BTCUSDT","marginType":"CROSSED","isAutoAddMargin":"false","leverage":21,"maxNotionalValue":"1000000"}]""");

        SymbolConfigEntry entry = Assert.Single(model.Symbols);
        Assert.Equal(Symbol.Parse("BTCUSDT"), entry.Symbol);
        Assert.Equal(MarginType.CROSS, entry.MarginType);
        Assert.False(entry.IsAutoAddMargin);
        Assert.Equal(21, entry.Leverage);
        Assert.Equal(1000000, entry.MaxNotionalValue);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("\"true\"", true)]
    [InlineData("false", false)]
    [InlineData("\"false\"", false)]
    public async Task AutoAddMarginReadsAsBooleanOrText(string value, bool expected)
    {
        SymbolConfigModel model = await ParseAsync<SymbolConfigParser, SymbolConfigModel>(
            """[{"symbol":"BTCUSDT","marginType":"ISOLATED","isAutoAddMargin":""" + value + ""","leverage":5,"maxNotionalValue":"1"}]""");

        Assert.Equal(expected, Assert.Single(model.Symbols).IsAutoAddMargin);
    }

    [Fact]
    public async Task ConfigWithUnknownMarginTypeOrLongSymbolStillReads()
    {
        SymbolConfigModel model = await ParseAsync<SymbolConfigParser, SymbolConfigModel>(
            """[{"symbol":"ABCDEFGHIJKLMNOPQ","marginType":"CROSSED","isAutoAddMargin":false,"leverage":5,"maxNotionalValue":"1"},{"symbol":"BTCUSDT","marginType":"PORTFOLIO","isAutoAddMargin":false,"leverage":5,"maxNotionalValue":"1"}]""");

        SymbolConfigEntry entry = Assert.Single(model.Symbols);
        Assert.Equal(Symbol.Parse("BTCUSDT"), entry.Symbol);
        Assert.Equal(MarginType.UNKNOWN, entry.MarginType);
    }

    [Theory]
    [InlineData("""[{"symbol":"BTCUSDT","marginType":"CROSSED","isAutoAddMargin":false,"maxNotionalValue":"1"}]""")]
    [InlineData("""[{"symbol":"BTCUSDT","marginType":"CROSSED","isAutoAddMargin":"maybe","leverage":5,"maxNotionalValue":"1"}]""")]
    [InlineData("""[{"marginType":"CROSSED","isAutoAddMargin":false,"leverage":5,"maxNotionalValue":"1"}]""")]
    public async Task IncompleteSymbolConfigIsMalformed(string text)
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync<SymbolConfigParser, SymbolConfigModel>(text));
    }

    [Fact]
    public async Task ReadsChangedLeverage()
    {
        ChangeLeverageModel model = await ParseAsync<ChangeLeverageParser, ChangeLeverageModel>(
            """{"leverage":21,"maxNotionalValue":"1000000","symbol":"BTCUSDT"}""");

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(21, model.Leverage);
        Assert.Equal(1000000, model.MaxNotionalValue);
    }

    [Theory]
    [InlineData("""{"maxNotionalValue":"1000000","symbol":"BTCUSDT"}""")]
    [InlineData("""{"leverage":21,"symbol":"BTCUSDT"}""")]
    [InlineData("""{"leverage":21,"maxNotionalValue":"1000000"}""")]
    public async Task ChangedLeverageWithoutAnyFieldIsMalformed(string text)
    {
        await Assert.ThrowsAsync<JsonException>(() => ParseAsync<ChangeLeverageParser, ChangeLeverageModel>(text));
    }

    private static string PositionsV3(int count)
    {
        StringBuilder body = new("""{"positions":[""");

        for (int i = 0; i < count; i++)
        {
            body.Append(i == 0 ? "" : ",");
            body.Append(CultureInfo.InvariantCulture, $$"""{"symbol":"S{{i}}USDT","positionSide":"BOTH","positionAmt":"1","updateTime":{{i}}}""");
        }

        return body.Append("]}").ToString();
    }

    private static async Task<TValue> ParseAsync<TParser, TValue>(string text) where TParser : struct, IResponseParser<TValue>
    {
        using MemoryStream body = new(Encoding.ASCII.GetBytes(text));
        return await default(TParser).ParseAsync(body, TestContext.Current.CancellationToken);
    }
}
