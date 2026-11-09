using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Samples.Handlers;

namespace Axitor.Trading.Gateway.Samples.CoinM;

/// <summary>
/// COIN-M futures. Streams need FuturesCoinM in BinanceOptions.StreamMarkets; user data also needs it in WsApiMarkets.
/// A call on a market left out of the options throws InvalidOperationException.
/// Quantities are in contracts, not coins.
/// </summary>
internal static class CoinMSamples
{
    // COIN-M symbols name the contract, e.g. a perpetual.
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSD_PERP");

    /// <summary>Exchange server time. Also a quick check that the REST API is reachable.</summary>
    public static async Task ServerTimeAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ServerTimeModel> result = await client.Futures.CoinM.RestApi.MarketData.ServerTime.GetAsync().ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Server time: {result.Value.ServerTime}" : $"Server time failed: {result.Errors.Reason}");
    }

    /// <summary>Order book snapshot in new arrays. Simple, but allocates on every call.</summary>
    public static async Task OrderBookAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        // Levels per side: 5, 10, 20, 50, 100, 500 or 1000. More levels cost more weight.
        RestApiRequestResult<OrderBookModel> result = await client.Futures.CoinM.RestApi.MarketData.OrderBook
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

        RestApiRequestResult<OrderBookSnapshotModel> result = await client.Futures.CoinM.RestApi.MarketData.OrderBook
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

    /// <summary>Local order book kept in sync by OrderBookSet. Use one OrderBookSet per depth session.</summary>
    public static async Task DepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // The set loads its snapshots from the COIN-M REST API.
        OrderBookSet<NoOpTelemetryHandler> books = new(client, BinanceMarkets.FuturesCoinM);
        BinanceStreamSession session = client.Futures.CoinM.WsStreams.Depth.CreateSession(books.Handler, DepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        // No handle: rejected or no room. Unconfirmed still has a handle: data comes once the exchange applies it.
        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            // False while the book is not synced: before its first snapshot and after a break.
            Console.WriteLine(books.TryGetBest(subscription.Handle, out DepthLevel bid, out DepthLevel ask, out _)
                ? $"Best bid {bid.Price}, best ask {ask.Price}"
                : "Syncing");
        }
    }

    /// <summary>
    /// Your orders, balances and positions in real time. Signed: the client needs an API key and secret.
    /// COIN-M sends no TradeLite or AlgoUpdate: asking for them throws ArgumentException.
    /// </summary>
    public static async Task UserDataAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        BinanceUserDataSession session = client.Futures.CoinM.WsStreams.UserData.CreateSession(new SampleUserDataHandler());

        BinanceUserDataEvents events =
            BinanceUserDataEvents.OrderTradeUpdate
            | BinanceUserDataEvents.AccountUpdate
            | BinanceUserDataEvents.AccountConfigUpdate
            | BinanceUserDataEvents.MarginCall;

        WsStreamSubscribeStatus status = await session.SubscribeAsync(events, appToken).ConfigureAwait(false);

        // Load open orders and balances after subscribing, so no change falls between them and the events.
        Console.WriteLine($"User data: {status}");
    }
}
