using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Samples.Spot;

/// <summary>Spot REST market data. Public endpoints: no keys needed.</summary>
internal static class SpotRestSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Exchange server time. Also a quick check that the REST API is reachable.</summary>
    public static async Task ServerTimeAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Spot.RestApi.MarketData.ServerTime.GetAsync().ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Server time: {result.Value.ServerTime}" : $"Server time failed: {result.Errors.Reason}");
    }

    /// <summary>Order book snapshot in new arrays. Simple, but allocates on every call.</summary>
    public static async Task OrderBookAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        // Levels per side: 1 to 5000. More levels cost more weight: up to 100 costs 5.
        RestApiRequestResult<OrderBookModel> result = await client.Spot.RestApi.MarketData.OrderBook
            .GetAsync(_symbol, limit: 100)
            .ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Order book failed: {result.Errors.Reason}");
            return;
        }

        OrderBookModel book = result.Value;

        // Best levels come first.
        if (0 < book.Bids.Length && 0 < book.Asks.Length)
        {
            Console.WriteLine($"Update id {book.LastUpdateId}, best bid {book.Bids[0].Price}, best ask {book.Asks[0].Price}");
        }
    }

    /// <summary>Order book snapshot written into your buffers. Nothing is allocated for the levels.</summary>
    public static async Task OrderBookIntoBuffersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        const int Limit = 1000;

        // Create the buffers once and reuse them. Each must hold at least Limit levels.
        DepthLevel[] bids = new DepthLevel[Limit];
        DepthLevel[] asks = new DepthLevel[Limit];

        RestApiRequestResult<OrderBookSnapshotModel> result = await client.Spot.RestApi.MarketData.OrderBook
            .GetAsync(_symbol, bids, asks, Limit)
            .ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Order book failed: {result.Errors.Reason}");
            return;
        }

        // BidCount and AskCount say how many levels were written, best first.
        OrderBookSnapshotModel snapshot = result.Value;

        if (0 < snapshot.BidCount && 0 < snapshot.AskCount)
        {
            Console.WriteLine($"Update id {snapshot.LastUpdateId}, best bid {bids[0].Price}, best ask {asks[0].Price}");
        }
    }
}
