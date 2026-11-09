using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Samples.Handlers;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>
/// USD-M futures market streams. Public: no keys needed. Needs FuturesUsdM in BinanceOptions.StreamMarkets, which is on by default.
/// A session holds one stream type for many symbols. Create it once and keep it as long as the client.
/// Its connections open on the first subscription and reconnect by themselves.
/// Session.Completion faults with WsStreamFatalException once the session stops for good: watch it and stop trading.
/// </summary>
internal static class UsdMStreamSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Best bid and ask in real time, kept in a LatestCache: you always read the latest value.</summary>
    public static async Task BookTickerAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // One slot per subscription. Size it by the session capacity.
        LatestCache<BookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(cache.Handler);

        // Subscribe as many symbols as you need. Each one gets a handle.
        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        // No handle: rejected or no room. Unconfirmed still has a handle: data comes once the exchange applies it.
        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        // Read any time, from any thread. False before the first message and during a connection break.
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out BookTickerModel ticker, out long version))
            {
                Console.WriteLine($"{ticker.Symbol}: bid {ticker.BidPrice}, ask {ticker.AskPrice}, message {version}");
            }
        }
    }

    /// <summary>Best bid and ask with your own handler. It is called on the receive loop for every message.</summary>
    public static async Task BookTickerWithOwnHandlerAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        SampleBookTickerHandler handler = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.BookTicker.CreateSession(handler);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), appToken).ConfigureAwait(false);
        Console.WriteLine($"Mid price: {handler.GetMidPrice(subscription.Handle)}");

        // Stop the stream when you no longer need it. The handle may then go to another symbol.
        bool confirmed = await session.UnsubscribeAsync(subscription.Handle, appToken).ConfigureAwait(false);
        Console.WriteLine($"Unsubscribed, confirmed: {confirmed}");
    }

    /// <summary>Market trades in a ChannelSink: a queue where every message counts. One consumer reads it.</summary>
    public static async Task AggTradeAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // Holds up to 10 000 trades. When the consumer falls behind, new trades are dropped and an Overflow item says so.
        ChannelSink<AggTradeModel> sink = new(capacity: 10_000, client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.AggTrade.CreateSession(sink.Handler);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        try
        {
            while (await sink.WaitToReadAsync(appToken).ConfigureAwait(false))
            {
                while (sink.TryRead(out StreamItem<AggTradeModel> item))
                {
                    switch (item.Kind)
                    {
                        case StreamItemKind.Data:
                            Console.WriteLine($"{item.Model.Symbol}: {item.Model.Quantity} at {item.Model.Price}, taker sold {item.Model.IsBuyerMaker}");
                            break;

                        case StreamItemKind.Disconnected:
                            Console.WriteLine($"Data stopped: {item.Reason}");
                            break;

                        case StreamItemKind.Reconnected:
                        case StreamItemKind.Overflow:
                            // Trades inside the gap are lost. Rebuild anything made from them, e.g. volume.
                            Console.WriteLine($"Lost trades from {item.Gap.StartUtcMillis} to {item.Gap.EndUtcMillis}, dropped {item.DroppedCount}");
                            break;

                        case StreamItemKind.Unsubscribed:
                            Console.WriteLine("Unsubscribed");
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
        catch (WsStreamFatalException exception)
        {
            // The session stopped for good. The full cause is in session.Completion.
            Console.WriteLine($"Stream stopped: {exception.Disconnect.Reason}");
        }
    }

    /// <summary>
    /// Local order book kept in sync by OrderBookSet: it loads snapshots and applies depth updates by itself.
    /// Read it from any thread. Use one OrderBookSet per depth session.
    /// </summary>
    public static async Task DepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // Snapshot levels per side. More levels give a deeper book but cost more weight.
        OrderBookSetOptions options = new() { SnapshotLimit = 1000 };

        OrderBookSet<NoOpTelemetryHandler> books = new(client, BinanceMarkets.FuturesUsdM, options);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.Depth.CreateSession(books.Handler, DepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        // Buffers for the top levels. Create them once and reuse them.
        DepthLevel[] topBids = new DepthLevel[10];
        DepthLevel[] topAsks = new DepthLevel[10];

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            // False while the book is not synced: before its first snapshot and after a break.
            if (!books.TryGetBest(subscription.Handle, out DepthLevel bid, out DepthLevel ask, out long updateId))
            {
                Console.WriteLine("Syncing");
                continue;
            }

            Console.WriteLine($"Best bid {bid.Price} x {bid.Quantity}, best ask {ask.Price} x {ask.Quantity}, update {updateId}");

            // Top levels, best first.
            if (books.TryCopyTop(subscription.Handle, topBids, topAsks, out int bidCount, out int askCount, out _))
            {
                Console.WriteLine($"Top {bidCount} bids, top {askCount} asks, deepest bid {topBids[bidCount - 1].Price}");
            }
        }

        Console.WriteLine($"Resyncs so far: {books.ResyncCount}");
    }

    /// <summary>Depth updates with your own handler, e.g. to build your own order book.</summary>
    public static async Task DepthWithOwnHandlerAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        SampleDepthHandler handler = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.Depth.CreateSession(handler, DepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), appToken).ConfigureAwait(false);
        Console.WriteLine($"Last update id: {handler.GetLastUpdateId(subscription.Handle)}");
    }

    /// <summary>Top 20 bids and asks, sent whole on every push: no snapshot or sync needed. Kept in a LatestCache.</summary>
    public static async Task PartialDepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<PartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.PartialDepth.CreateSession(cache.Handler, PartialDepthLevels.Twenty, DepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out PartialDepthModel depth, out _))
            {
                PrintTopLevels(in depth);
            }
        }
    }

    /// <summary>The current candle, kept in a LatestCache. Each message holds the whole candle; IsClosed marks its final state.</summary>
    public static async Task KlineAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<KlineUpdateModel> cache = new(client.StreamSessionCapacity);

        // One interval per session. Make another session for another interval.
        BinanceStreamSession session = client.Futures.UsdM.WsStreams.Kline.CreateSession(cache.Handler, BinanceInterval.OneMinute);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out KlineUpdateModel update, out _))
            {
                Console.WriteLine($"{update.Symbol} {update.Kline.OpenTime}: close {update.Kline.Close}, volume {update.Kline.Volume}, closed {update.IsClosed}");
            }
        }
    }

    /// <summary>Only the first BidCount and AskCount levels are set.</summary>
    private static void PrintTopLevels(in PartialDepthModel depth)
    {
        ReadOnlySpan<DepthLevel> bids = depth.Bids[..depth.BidCount];
        ReadOnlySpan<DepthLevel> asks = depth.Asks[..depth.AskCount];

        if (bids.IsEmpty || asks.IsEmpty)
        {
            return;
        }

        Console.WriteLine($"{depth.Symbol}: {bids.Length} bids from {bids[0].Price}, {asks.Length} asks from {asks[0].Price}");
    }
}
