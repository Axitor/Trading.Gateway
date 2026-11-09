using System.Net;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Public;

/// <summary>Public WebSocket API market data of USD-M and spot, read from mainnet without keys.</summary>
public sealed class WsApiMarketDataLiveTests
{
    private const int BookLimit = 5;

    // The last trade can sit a little outside the book read a moment later.
    private const double PriceBand = 0.01;

    // Wide enough for a quiet minute between two trades and the skew between exchange servers.
    private const long FreshnessMillis = 60_000;

    // -1121 BAD_SYMBOL: Invalid symbol.
    private const int InvalidSymbolCode = -1121;

    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");
    private static readonly Symbol _unlistedSymbol = Symbol.Parse("AXITORUSDT");

    // Listed on both markets.
    private static readonly Symbol[] _burstSymbols =
    [
        Symbol.Parse("BTCUSDT"),
        Symbol.Parse("ETHUSDT"),
        Symbol.Parse("BNBUSDT"),
        Symbol.Parse("SOLUSDT"),
        Symbol.Parse("XRPUSDT"),
        Symbol.Parse("DOGEUSDT"),
        Symbol.Parse("ADAUSDT"),
        Symbol.Parse("LTCUSDT"),
        Symbol.Parse("LINKUSDT"),
        Symbol.Parse("AVAXUSDT"),
    ];

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.Spot)]
    public async Task TickerPriceMatchesMarket(BinanceMarkets market)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, wsApiMarkets: market);

        WsApiRequestResult<TickerAnswer> result = await TickerAsync(client, market, _symbol);
        RestApiRequestResult<OrderBookModel> book = await OrderBookAsync(client, market);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(RequestOutcome.Success, book.Outcome);
        Assert.Equal(_symbol, result.Value.Symbol);
        Assert.InRange(result.Value.Price, book.Value.Bids[0].Price * (1 - PriceBand), book.Value.Asks[0].Price * (1 + PriceBand));

        await AssertTickerTimeAsync(client, market, result.Value.Time);
        log.AssertContractKept();
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.Spot)]
    public async Task ConcurrentRequestsGetTheirOwnAnswers(BinanceMarkets market)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, wsApiMarkets: market);
        Task<WsApiRequestResult<TickerAnswer>>[] requests = new Task<WsApiRequestResult<TickerAnswer>>[_burstSymbols.Length];

        for (int i = 0; i < requests.Length; i++)
        {
            requests[i] = TickerAsync(client, market, _burstSymbols[i]);
        }

        WsApiRequestResult<TickerAnswer>[] results = await Task.WhenAll(requests);
        long[] requestIds = new long[results.Length];

        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(RequestOutcome.Success, results[i].Outcome);
            Assert.Equal(_burstSymbols[i], results[i].Value.Symbol);
            requestIds[i] = results[i].RequestId;
        }

        Assert.Distinct(requestIds);
        log.AssertContractKept();
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.Spot)]
    public async Task UnlistedSymbolIsRejectedAndClientStaysUsable(BinanceMarkets market)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false, wsApiMarkets: market);

        WsApiRequestResult<TickerAnswer> rejected = await TickerAsync(client, market, _unlistedSymbol);
        WsApiRequestResult<TickerAnswer> next = await TickerAsync(client, market, _symbol);

        Assert.Equal(RequestOutcome.Failed, rejected.Outcome);
        Assert.Equal(InvalidSymbolCode, rejected.Errors.ExchangeCode);
        Assert.Equal((int)HttpStatusCode.BadRequest, rejected.Errors.StatusCode);
        Assert.Equal(WsApiRequestErrorReason.ExchangeApiRequestError, rejected.Errors.Reason);
        Assert.False(string.IsNullOrEmpty(rejected.Errors.ExchangeMessage));
        Assert.Equal(RequestErrorAction.Skip, rejected.Resiliency.Action);
        Assert.Equal(0, rejected.Resiliency.RetryNumber);

        // An exchange error is an answer, not a broken connection: the next request goes through.
        Assert.Equal(RequestOutcome.Success, next.Outcome);
        Assert.Equal(_symbol, next.Value.Symbol);
        log.AssertContractKept();
    }

    private static async Task<WsApiRequestResult<TickerAnswer>> TickerAsync(BinanceClient<LiveTelemetry> client, BinanceMarkets market, Symbol symbol)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        if (market == BinanceMarkets.FuturesUsdM)
        {
            WsApiRequestResult<Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice.TickerPriceModel> usdM =
                await client.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(symbol, cancellationToken: cancellationToken);

            return Answer(in usdM, new TickerAnswer { Symbol = usdM.Value.Symbol, Price = usdM.Value.Price, Time = usdM.Value.Time });
        }

        if (market == BinanceMarkets.Spot)
        {
            WsApiRequestResult<Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData.TickerPrice.TickerPriceModel> spot =
                await client.Spot.WsApi.MarketData.TickerPrice.GetAsync(symbol, cancellationToken: cancellationToken);

            return Answer(in spot, new TickerAnswer { Symbol = spot.Value.Symbol, Price = spot.Value.Price });
        }

        throw new ArgumentOutOfRangeException(nameof(market));
    }

    private static WsApiRequestResult<TickerAnswer> Answer<TModel>(in WsApiRequestResult<TModel> result, TickerAnswer value) =>
        new()
        {
            Value = value,
            RequestId = result.RequestId,
            Errors = result.Errors,
            Latency = result.Latency,
            Resiliency = result.Resiliency,
            Outcome = result.Outcome,
        };

    private static ValueTask<RestApiRequestResult<OrderBookModel>> OrderBookAsync(BinanceClient<LiveTelemetry> client, BinanceMarkets market)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(_symbol, BookLimit, cancellationToken: cancellationToken),
            BinanceMarkets.Spot => client.Spot.RestApi.MarketData.OrderBook.GetAsync(_symbol, BookLimit, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };
    }

    /// <summary>USD-M price time is checked against the server time; spot sends none.</summary>
    private static async Task AssertTickerTimeAsync(BinanceClient<LiveTelemetry> client, BinanceMarkets market, long tickerTime)
    {
        if (market != BinanceMarkets.FuturesUsdM)
        {
            return;
        }

        RestApiRequestResult<ServerTimeModel> time = await client.Futures.UsdM.RestApi.MarketData.ServerTime
            .GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, time.Outcome);
        Assert.InRange(tickerTime, time.Value.ServerTime - FreshnessMillis, time.Value.ServerTime + FreshnessMillis);
    }

    /// <summary>Ticker of either market. Spot sends no price time, so Time stays zero there.</summary>
    private struct TickerAnswer
    {
        public Symbol Symbol;
        public double Price;
        public long Time;
    }
}
