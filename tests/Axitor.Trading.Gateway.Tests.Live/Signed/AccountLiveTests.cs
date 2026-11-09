using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Signed;

/// <summary>USD-M account reads over REST and the WebSocket API. Read only: nothing in the account changes.</summary>
public sealed class AccountLiveTests
{
    // The exchange sends 8 decimals; sums are compared with room for its rounding.
    private const double Tolerance = 1e-6;

    // -1022 INVALID_SIGNATURE: Signature for this request is not valid.
    private const int InvalidSignatureCode = -1022;

    // Any secret other than the key's own signs every request wrong.
    private const string WrongSecret = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _usdt = Symbol.Parse("USDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AccountTotalsAddUp()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        RestApiRequestResult<AccountInfoModel> result = await client.Futures.UsdM.RestApi.Account.AccountInfo.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        AccountInfoModel account = result.Value;

        Assert.NotNull(account.Assets);
        Assert.NotNull(account.Positions);
        AssertClose(account.TotalWalletBalance + account.TotalUnrealizedProfit, account.TotalMarginBalance, "total margin balance");

        foreach (AssetBalance asset in account.Assets)
        {
            AssertClose(asset.WalletBalance + asset.UnrealizedProfit, asset.MarginBalance, $"{asset.Asset} margin balance");
        }

        // Live tests run on a one-way account.
        foreach (AccountPosition position in account.Positions)
        {
            Assert.Equal(PositionSide.BOTH, position.Side);
        }
    }

    [Fact]
    public async Task WsApiAndRestSeeTheSameAccount()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true, wsApiMarkets: BinanceMarkets.FuturesUsdM);

        RestApiRequestResult<AccountInfoV2Model> rest = await client.Futures.UsdM.RestApi.Account.AccountInfoV2.GetAsync(cancellationToken: Token);
        WsApiRequestResult<AccountInfoV2Model> wsApi = await client.Futures.UsdM.WsApi.Account.AccountInfo.GetAsync(cancellationToken: Token);
        WsApiRequestResult<AccountBalanceModel> balance = await client.Futures.UsdM.WsApi.Account.AccountBalance.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, rest.Outcome);
        Assert.Equal(RequestOutcome.Success, wsApi.Outcome);
        Assert.Equal(RequestOutcome.Success, balance.Outcome);

        AccountInfoV2Model restAccount = rest.Value;
        AccountInfoV2Model wsAccount = wsApi.Value;

        Assert.Equal(restAccount.CanTrade, wsAccount.CanTrade);
        Assert.Equal(restAccount.CanDeposit, wsAccount.CanDeposit);
        Assert.Equal(restAccount.CanWithdraw, wsAccount.CanWithdraw);
        Assert.Equal(restAccount.FeeTier, wsAccount.FeeTier);
        Assert.Equal(restAccount.IsMultiAssetsMargin, wsAccount.IsMultiAssetsMargin);
        Assert.Equal(restAccount.TradeGroupId, wsAccount.TradeGroupId);
        Assert.Equal(restAccount.TotalWalletBalance, wsAccount.TotalWalletBalance);

        // Both list every symbol of the market, with or without a position.
        Assert.Equal(restAccount.Positions.Length, wsAccount.Positions.Length);

        AccountInfoV2Position restPosition = FindPosition(restAccount.Positions, _symbol);
        AccountInfoV2Position wsPosition = FindPosition(wsAccount.Positions, _symbol);

        Assert.Equal(restPosition.Leverage, wsPosition.Leverage);
        Assert.Equal(restPosition.IsIsolated, wsPosition.IsIsolated);
        Assert.Equal(restPosition.MaxNotional, wsPosition.MaxNotional);

        Assert.Equal(FindAsset(restAccount.Assets, _usdt).WalletBalance, FindBalance(balance.Value.Assets, _usdt).WalletBalance);
        log.AssertContractKept();
    }

    [Fact]
    public async Task SymbolConfigMatchesAccount()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        RestApiRequestResult<SymbolConfigModel> one = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAsync(_symbol, cancellationToken: Token);
        RestApiRequestResult<SymbolConfigModel> all = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAllAsync(cancellationToken: Token);
        RestApiRequestResult<AccountInfoV2Model> account = await client.Futures.UsdM.RestApi.Account.AccountInfoV2.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, one.Outcome);
        Assert.Equal(RequestOutcome.Success, all.Outcome);
        Assert.Equal(RequestOutcome.Success, account.Outcome);

        SymbolConfigEntry entry = Assert.Single(one.Value.Symbols);
        Assert.Equal(_symbol, entry.Symbol);

        foreach (SymbolConfigEntry item in all.Value.Symbols)
        {
            Assert.True(item.MarginType != MarginType.UNKNOWN, $"{item.Symbol} has a margin type this version does not know.");
            Assert.True(0 < item.Leverage, $"{item.Symbol} has no leverage.");
        }

        int index = Array.FindIndex(all.Value.Symbols, item => item.Symbol == _symbol);
        Assert.True(0 <= index, $"{_symbol} is not listed.");

        SymbolConfigEntry fromAll = all.Value.Symbols[index];
        AccountInfoV2Position position = FindPosition(account.Value.Positions, _symbol);

        Assert.Equal(entry.Leverage, fromAll.Leverage);
        Assert.Equal(entry.MarginType, fromAll.MarginType);
        Assert.Equal(entry.MaxNotionalValue, fromAll.MaxNotionalValue);

        Assert.Equal(position.Leverage, entry.Leverage);
        Assert.Equal(position.IsIsolated, entry.MarginType == MarginType.ISOLATED);
        Assert.Equal(position.MaxNotional, entry.MaxNotionalValue);
    }

    [Fact]
    public async Task LeverageBracketsChainWithoutGaps()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        RestApiRequestResult<LeverageBracketModel> result = await client.Futures.UsdM.RestApi.Account.LeverageBracket.GetAsync(_symbol, cancellationToken: Token);
        RestApiRequestResult<SymbolConfigModel> config = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAsync(_symbol, cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(RequestOutcome.Success, config.Outcome);

        SymbolBrackets symbol = Assert.Single(result.Value.Symbols);
        LeverageBracketLevel[] brackets = symbol.Brackets;

        Assert.Equal(_symbol, symbol.Symbol);
        Assert.NotEmpty(brackets);
        Assert.Equal(0, brackets[0].NotionalFloor);
        Assert.Equal(0, brackets[0].Cum);

        for (int i = 0; i < brackets.Length; i++)
        {
            LeverageBracketLevel level = brackets[i];

            Assert.Equal(i + 1, level.Bracket);
            Assert.True(level.NotionalFloor < level.NotionalCap, $"Bracket {level.Bracket} has an empty range.");
            Assert.True(0 < level.InitialLeverage && 0 < level.MaintMarginRatio, $"Bracket {level.Bracket} has no leverage or margin ratio.");

            if (0 < i)
            {
                LeverageBracketLevel previous = brackets[i - 1];

                Assert.Equal(previous.NotionalCap, level.NotionalFloor);
                Assert.True(level.InitialLeverage < previous.InitialLeverage, $"Bracket {level.Bracket} has no lower leverage.");
                Assert.True(previous.MaintMarginRatio < level.MaintMarginRatio, $"Bracket {level.Bracket} has no higher margin ratio.");

                // Cum keeps the maintenance margin continuous at the border of two brackets.
                double expectedCum = previous.Cum + level.NotionalFloor * (level.MaintMarginRatio - previous.MaintMarginRatio);
                AssertClose(expectedCum, level.Cum, $"cum of bracket {level.Bracket}");
            }
        }

        Assert.InRange(Assert.Single(config.Value.Symbols).Leverage, 1, brackets[0].InitialLeverage);
    }

    [Fact]
    public async Task OpenOrdersOfSymbolAreReadable()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        RestApiRequestResult<OpenOrdersModel> orders = await client.Futures.UsdM.RestApi.Trade.OpenOrders.GetAsync(_symbol, cancellationToken: Token);
        RestApiRequestResult<OpenAlgoOrdersModel> algoOrders = await client.Futures.UsdM.RestApi.Trade.OpenAlgoOrders.GetAsync(_symbol, cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, orders.Outcome);
        Assert.Equal(RequestOutcome.Success, algoOrders.Outcome);

        // Live tests place no orders, so the lists are usually empty: an empty answer must still be an empty array.
        Assert.NotNull(orders.Value.Orders);
        Assert.NotNull(algoOrders.Value.Orders);

        foreach (OrderModel order in orders.Value.Orders)
        {
            Assert.Equal(_symbol, order.Symbol);
            Assert.True(order.Status is OrderStatus.NEW or OrderStatus.PARTIALLY_FILLED, $"Order {order.OrderId} is listed as open with status {order.Status}.");
        }

        foreach (AlgoOrderModel order in algoOrders.Value.Orders)
        {
            Assert.Equal(_symbol, order.Symbol);
            Assert.True(order.Status != AlgoStatus.UNKNOWN, $"Algo order {order.AlgoId} has a status this version does not know.");
        }
    }

    [Fact]
    public async Task WrongSecretStopsWithoutRetry()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true, apiSecret: WrongSecret);

        // A bad signature never heals by itself: the client stops at once instead of retrying.
        RestApiFatalException exception = await Assert.ThrowsAsync<RestApiFatalException>(
            async () => await client.Futures.UsdM.RestApi.Account.AccountInfo.GetAsync(cancellationToken: Token));

        log.Report($"Fatal: {exception.Errors.Reason}, HTTP {exception.Errors.StatusCode}, code {exception.Errors.ExchangeCode} {exception.Errors.ExchangeMessage}");

        Assert.Equal(InvalidSignatureCode, exception.Errors.ExchangeCode);
        Assert.False(string.IsNullOrEmpty(exception.Errors.ExchangeMessage));
        Assert.Equal(RequestErrorAction.Fatal, exception.Resiliency.Action);
        Assert.Equal(0, exception.Resiliency.RetryNumber);
    }

    private static AccountInfoV2Position FindPosition(AccountInfoV2Position[] positions, Symbol symbol)
    {
        int index = Array.FindIndex(positions, item => item.Symbol == symbol);

        Assert.True(0 <= index, $"{symbol} is not listed.");
        return positions[index];
    }

    private static AccountInfoV2Asset FindAsset(AccountInfoV2Asset[] assets, Symbol asset)
    {
        int index = Array.FindIndex(assets, item => item.Asset == asset);

        Assert.True(0 <= index, $"{asset} is not listed.");
        return assets[index];
    }

    private static AccountBalanceAsset FindBalance(AccountBalanceAsset[] assets, Symbol asset)
    {
        int index = Array.FindIndex(assets, item => item.Asset == asset);

        Assert.True(0 <= index, $"{asset} is not listed in the balance.");
        return assets[index];
    }

    private static void AssertClose(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"The {what} is {actual}, expected {expected}.");
}
