using System.Net;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Models.Responses;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Signed;

/// <summary>USD-M test orders: the exchange checks them and places nothing.</summary>
public sealed class TestOrderLiveTests
{
    // Well inside the mark price band, so the order is valid for a real placement too.
    private const double NearBidFactor = 0.99;

    // Room over the minimum notional for a price move between the book read and the order.
    private const double NotionalHeadroom = 1.5;

    // -1121 BAD_SYMBOL: Invalid symbol.
    private const int InvalidSymbolCode = -1121;

    private const int BookLimit = 5;

    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _unlistedSymbol = Symbol.Parse("AXITORUSDT");
    private static readonly ClientOrderId _clientOrderId = ClientOrderId.Parse("axitor-live-test-order");

    [Fact]
    public async Task ValidOrderPasses()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        SymbolFilters filters = await LoadFiltersAsync(client);
        NewOrderRequest request = BuyLimit(_symbol, in filters, await BestBidAsync(client) * NearBidFactor);

        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.TestOrder
            .TestAsync(in request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task UnlistedSymbolIsRejectedByExchangeWithoutRetry()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true);

        // Valid price and size of a listed symbol, so only the symbol itself can fail on the exchange.
        SymbolFilters filters = await LoadFiltersAsync(client);
        NewOrderRequest request = BuyLimit(_unlistedSymbol, in filters, await BestBidAsync(client) * NearBidFactor);

        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.TestOrder
            .TestAsync(in request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Failed, result.Outcome);
        Assert.Equal(InvalidSymbolCode, result.Errors.ExchangeCode);
        Assert.Equal((int)HttpStatusCode.BadRequest, result.Errors.StatusCode);
        Assert.Equal(RestApiRequestErrorReason.ExchangeApiRequestError, result.Errors.Reason);
        Assert.False(string.IsNullOrEmpty(result.Errors.ExchangeMessage));
        Assert.Equal(RequestErrorAction.Skip, result.Resiliency.Action);
        Assert.Equal(0, result.Resiliency.RetryNumber);
    }

    private static async Task<SymbolFilters> LoadFiltersAsync(BinanceClient<LiveTelemetry> client)
    {
        RestApiRequestResult<ExchangeInfoModel> result = await client.Futures.UsdM.RestApi.MarketData.ExchangeInfo
            .GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        ExchangeSymbol[] symbols = result.Value.Symbols;
        int index = Array.FindIndex(symbols, static item => item.Symbol == _symbol);

        Assert.True(0 <= index, $"{_symbol} is not listed.");
        Assert.Equal(SymbolStatus.TRADING, symbols[index].Status);

        return symbols[index].Filters;
    }

    private static async Task<double> BestBidAsync(BinanceClient<LiveTelemetry> client)
    {
        RestApiRequestResult<OrderBookModel> result = await client.Futures.UsdM.RestApi.MarketData.OrderBook
            .GetAsync(_symbol, BookLimit, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.NotEmpty(result.Value.Bids);

        return result.Value.Bids[0].Price;
    }

    /// <summary>LIMIT BUY on the nearest tick, sized over the minimum notional at that price.</summary>
    private static NewOrderRequest BuyLimit(Symbol symbol, in SymbolFilters filters, double price)
    {
        double roundedPrice = BinanceOrderRules.RoundPrice(in filters, price);
        double rawQuantity = Math.Max(filters.MinNotional * NotionalHeadroom / roundedPrice, filters.MinQuantity);

        // Rounding goes down to the step, so one step on top keeps the size at or above the raw quantity.
        double quantity = BinanceOrderRules.RoundQuantity(in filters, rawQuantity + filters.StepSize, isMarket: false);

        return new NewOrderRequest
        {
            Symbol = symbol,
            ClientOrderId = _clientOrderId,
            Filters = filters,
            Side = OrderSide.BUY,
            Type = OrderType.LIMIT,
            TimeInForce = TimeInForce.GTC,
            Price = roundedPrice,
            Quantity = quantity,
        };
    }
}
