using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>USD-M futures REST market data. Public endpoints: no keys needed.</summary>
internal static class UsdMRestMarketDataSamples
{
    // Parse a symbol once and reuse it.
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Exchange server time. Also a quick check that the REST API is reachable.</summary>
    public static async Task ServerTimeAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Server time failed: {result.Errors.Reason}");
            return;
        }

        Console.WriteLine($"Server time: {result.Value.ServerTime}");
    }

    /// <summary>Candles written into your buffer. Nothing is allocated for them. To get a time range, pass startTimeMs and endTimeMs.</summary>
    public static async Task KlinesAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        // Create the buffer once and reuse it. Its length is how many candles you ask for, 1 to 1500.
        Kline[] buffer = new Kline[500];

        RestApiRequestResult<int> result = await client.Futures.UsdM.RestApi.MarketData.KlinesData
            .GetAsync(_symbol, BinanceInterval.OneMinute, buffer)
            .ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Klines failed: {result.Errors.Reason}");
            return;
        }

        // Value is how many candles were written, oldest first. The last one may still be open.
        for (int i = 0; i < result.Value; i++)
        {
            Console.WriteLine($"{buffer[i].OpenTime}: open {buffer[i].Open}, close {buffer[i].Close}, volume {buffer[i].Volume}");
        }
    }

    /// <summary>Trading rules of every symbol. Load them on start: every order needs the filters of its symbol.</summary>
    public static async Task ExchangeInfoAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ExchangeInfoModel> result = await client.Futures.UsdM.RestApi.MarketData.ExchangeInfo.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Exchange info failed: {result.Errors.Reason}");
            return;
        }

        foreach (ExchangeSymbol symbol in result.Value.Symbols)
        {
            if (symbol.Symbol != _symbol)
            {
                continue;
            }

            Console.WriteLine($"{symbol.Symbol}: {symbol.Status}, tick {symbol.Filters.TickSize}, step {symbol.Filters.StepSize}, min notional {symbol.Filters.MinNotional}");
        }
    }

    /// <summary>Order book snapshot in new arrays. Simple, but allocates on every call.</summary>
    public static async Task OrderBookAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        // Levels per side: 5, 10, 20, 50, 100, 500 or 1000. More levels cost more weight.
        RestApiRequestResult<OrderBookModel> result = await client.Futures.UsdM.RestApi.MarketData.OrderBook
            .GetAsync(_symbol, limit: 100)
            .ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Order book failed: {result.Errors.Reason}");
            return;
        }

        OrderBookModel book = result.Value;
        Console.WriteLine($"Update id {book.LastUpdateId}, {book.Bids.Length} bids, {book.Asks.Length} asks");

        // Best levels come first.
        if (0 < book.Bids.Length && 0 < book.Asks.Length)
        {
            Console.WriteLine($"Best bid {book.Bids[0].Price} x {book.Bids[0].Quantity}, best ask {book.Asks[0].Price} x {book.Asks[0].Quantity}");
        }
    }

    /// <summary>
    /// Order book snapshot written into your buffers. Nothing is allocated for the levels.
    /// For a local order book kept in sync, use OrderBookSet with the depth stream instead.
    /// </summary>
    public static async Task OrderBookIntoBuffersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        const int Limit = 1000;

        // Create the buffers once and reuse them. Each must hold at least Limit levels.
        DepthLevel[] bids = new DepthLevel[Limit];
        DepthLevel[] asks = new DepthLevel[Limit];

        RestApiRequestResult<OrderBookSnapshotModel> result = await client.Futures.UsdM.RestApi.MarketData.OrderBook
            .GetAsync(_symbol, bids, asks, Limit)
            .ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Order book failed: {result.Errors.Reason}");
            return;
        }

        // BidCount and AskCount say how many levels were written, best first.
        OrderBookSnapshotModel snapshot = result.Value;
        Console.WriteLine($"Update id {snapshot.LastUpdateId}, {snapshot.BidCount} bids, {snapshot.AskCount} asks");

        if (0 < snapshot.BidCount && 0 < snapshot.AskCount)
        {
            Console.WriteLine($"Best bid {bids[0].Price}, best ask {asks[0].Price}");
        }
    }
}
