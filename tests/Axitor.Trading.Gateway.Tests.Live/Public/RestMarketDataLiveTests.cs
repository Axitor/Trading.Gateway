using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Public;

/// <summary>Public REST market data of every market, read from mainnet without keys.</summary>
public sealed class RestMarketDataLiveTests
{
    private const int BookLimit = 20;
    private const int KlineCount = 100;
    private const long OneMinuteMillis = 60_000;

    // Wide enough for a synced machine clock plus the round trip.
    private const long ClockToleranceMillis = 60_000;

    private static readonly Symbol _usdMSymbol = Symbol.Parse("BTCUSDT");

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.FuturesCoinM)]
    [InlineData(BinanceMarkets.Spot)]
    public async Task ServerTimeIsNow(BinanceMarkets market)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false);

        RestApiRequestResult<ServerTimeModel> result = await ServerTimeAsync(client, market);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.InRange(result.Value.ServerTime, now - ClockToleranceMillis, now + ClockToleranceMillis);
    }

    [Fact]
    public async Task ExchangeInfoKnowsEveryStatusAndHasFilters()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false);

        RestApiRequestResult<ExchangeInfoModel> result = await client.Futures.UsdM.RestApi.MarketData.ExchangeInfo
            .GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        ExchangeSymbol[] symbols = result.Value.Symbols;
        Assert.NotEmpty(symbols);

        foreach (ExchangeSymbol item in symbols)
        {
            Assert.True(item.Status != SymbolStatus.UNKNOWN, $"{item.Symbol} has a status this version does not know.");

            if (item.Status == SymbolStatus.TRADING)
            {
                Assert.True(0 < item.Filters.TickSize && 0 < item.Filters.StepSize && 0 < item.Filters.MinNotional, $"{item.Symbol} trades with empty filters.");
            }
        }

        int index = Array.FindIndex(symbols, static item => item.Symbol == _usdMSymbol);
        Assert.True(0 <= index, $"{_usdMSymbol} is not listed.");

        SymbolFilters filters = symbols[index].Filters;

        Assert.Equal(SymbolStatus.TRADING, symbols[index].Status);
        Assert.True(0 < filters.MinPrice && filters.MinPrice < filters.MaxPrice);
        Assert.True(0 < filters.MinQuantity && filters.MinQuantity < filters.MaxQuantity);
        Assert.True(0 < filters.MarketStepSize && 0 < filters.MarketMinQuantity && filters.MarketMinQuantity < filters.MarketMaxQuantity);
    }

    [Fact]
    public async Task KlinesFillBufferInOrder()
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false);
        Kline[] buffer = new Kline[KlineCount];

        RestApiRequestResult<int> result = await client.Futures.UsdM.RestApi.MarketData.KlinesData
            .GetAsync(_usdMSymbol, BinanceInterval.OneMinute, buffer, cancellationToken: TestContext.Current.CancellationToken);

        RestApiRequestResult<ServerTimeModel> time = await ServerTimeAsync(client, BinanceMarkets.FuturesUsdM);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(KlineCount, result.Value);
        Assert.Equal(RequestOutcome.Success, time.Outcome);

        for (int i = 0; i < buffer.Length; i++)
        {
            Kline candle = buffer[i];

            Assert.Equal(candle.OpenTime + OneMinuteMillis - 1, candle.CloseTime);
            Assert.True(candle.Low <= Math.Min(candle.Open, candle.Close) && Math.Max(candle.Open, candle.Close) <= candle.High, $"Candle {candle.OpenTime} has prices outside its range.");
            Assert.True(0 <= candle.TakerBuyVolume && candle.TakerBuyVolume <= candle.Volume, $"Candle {candle.OpenTime} has taker volume above its volume.");

            if (0 < i)
            {
                Assert.Equal(buffer[i - 1].OpenTime + OneMinuteMillis, candle.OpenTime);
            }
        }

        // The newest candle is the current one, or the one just closed if a minute turned between the two calls.
        long lastOpenTime = buffer[^1].OpenTime;
        Assert.InRange(time.Value.ServerTime, lastOpenTime, lastOpenTime + 2 * OneMinuteMillis);
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM, "BTCUSDT")]
    [InlineData(BinanceMarkets.FuturesCoinM, "BTCUSD_PERP")]
    [InlineData(BinanceMarkets.Spot, "BTCUSDT")]
    public async Task OrderBookIsSortedAndUncrossed(BinanceMarkets market, string symbol)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false);

        RestApiRequestResult<OrderBookModel> result = await OrderBookAsync(client, market, Symbol.Parse(symbol));

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        OrderBookModel book = result.Value;

        Assert.Equal(BookLimit, book.Bids.Length);
        Assert.Equal(BookLimit, book.Asks.Length);
        Assert.True(0 < book.LastUpdateId);
        AssertBookTimes(market, book.EventTime, book.TransactionTime);
        AssertSortedAndUncrossed(book.Bids, book.Asks);
    }

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM, "BTCUSDT")]
    [InlineData(BinanceMarkets.FuturesCoinM, "BTCUSD_PERP")]
    [InlineData(BinanceMarkets.Spot, "BTCUSDT")]
    public async Task OrderBookIntoBuffersIsSortedAndUncrossed(BinanceMarkets market, string symbol)
    {
        LiveTelemetryLog log = new();
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: false);
        DepthLevel[] bids = new DepthLevel[BookLimit];
        DepthLevel[] asks = new DepthLevel[BookLimit];

        RestApiRequestResult<OrderBookSnapshotModel> result = await OrderBookIntoAsync(client, market, Symbol.Parse(symbol), bids, asks);

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        OrderBookSnapshotModel snapshot = result.Value;

        Assert.Equal(BookLimit, snapshot.BidCount);
        Assert.Equal(BookLimit, snapshot.AskCount);
        Assert.True(0 < snapshot.LastUpdateId);
        AssertBookTimes(market, snapshot.EventTime, snapshot.TransactionTime);
        AssertSortedAndUncrossed(bids.AsSpan(0, snapshot.BidCount), asks.AsSpan(0, snapshot.AskCount));
    }

    private static ValueTask<RestApiRequestResult<ServerTimeModel>> ServerTimeAsync(BinanceClient<LiveTelemetry> client, BinanceMarkets market)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: cancellationToken),
            BinanceMarkets.FuturesCoinM => client.Futures.CoinM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: cancellationToken),
            BinanceMarkets.Spot => client.Spot.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };
    }

    private static ValueTask<RestApiRequestResult<OrderBookModel>> OrderBookAsync(BinanceClient<LiveTelemetry> client, BinanceMarkets market, Symbol symbol)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(symbol, BookLimit, cancellationToken: cancellationToken),
            BinanceMarkets.FuturesCoinM => client.Futures.CoinM.RestApi.MarketData.OrderBook.GetAsync(symbol, BookLimit, cancellationToken: cancellationToken),
            BinanceMarkets.Spot => client.Spot.RestApi.MarketData.OrderBook.GetAsync(symbol, BookLimit, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };
    }

    private static ValueTask<RestApiRequestResult<OrderBookSnapshotModel>> OrderBookIntoAsync(
        BinanceClient<LiveTelemetry> client,
        BinanceMarkets market,
        Symbol symbol,
        DepthLevel[] bids,
        DepthLevel[] asks)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return market switch
        {
            BinanceMarkets.FuturesUsdM => client.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, BookLimit, cancellationToken: cancellationToken),
            BinanceMarkets.FuturesCoinM => client.Futures.CoinM.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, BookLimit, cancellationToken: cancellationToken),
            BinanceMarkets.Spot => client.Spot.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, BookLimit, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };
    }

    /// <summary>Futures books carry event and transaction times; spot books carry neither.</summary>
    private static void AssertBookTimes(BinanceMarkets market, long eventTime, long transactionTime)
    {
        if (market == BinanceMarkets.Spot)
        {
            Assert.Equal(0, eventTime);
            Assert.Equal(0, transactionTime);
            return;
        }

        Assert.True(0 < eventTime && 0 < transactionTime);
    }

    /// <summary>Bids go down, asks go up, every level is positive and the best bid is below the best ask.</summary>
    private static void AssertSortedAndUncrossed(ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        for (int i = 0; i < bids.Length; i++)
        {
            Assert.True(0 < bids[i].Price && 0 < bids[i].Quantity, $"Bid {i} is not positive.");
            Assert.True(i == 0 || bids[i].Price < bids[i - 1].Price, $"Bid {i} is out of order.");
        }

        for (int i = 0; i < asks.Length; i++)
        {
            Assert.True(0 < asks[i].Price && 0 < asks[i].Quantity, $"Ask {i} is not positive.");
            Assert.True(i == 0 || asks[i - 1].Price < asks[i].Price, $"Ask {i} is out of order.");
        }

        Assert.True(bids[0].Price < asks[0].Price, "The book is crossed.");
    }
}
