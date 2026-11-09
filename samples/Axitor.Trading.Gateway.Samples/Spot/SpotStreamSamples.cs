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

namespace Axitor.Trading.Gateway.Samples.Spot;

/// <summary>
/// Spot market streams. Public: no keys needed. Needs Spot in BinanceOptions.StreamMarkets.
/// A session holds one stream type for many symbols. Create it once and keep it as long as the client.
/// Session.Completion faults with WsStreamFatalException once the session stops for good: watch it and stop trading.
/// </summary>
internal static class SpotStreamSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Best bid and ask in real time, kept in a LatestCache: you always read the latest value.</summary>
    public static async Task BookTickerAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<SpotBookTickerModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.WsStreams.BookTicker.CreateSession(cache.Handler);

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

            if (cache.TryRead(subscription.Handle, out SpotBookTickerModel ticker, out _))
            {
                Console.WriteLine($"{ticker.Symbol}: bid {ticker.BidPrice}, ask {ticker.AskPrice}");
            }
        }
    }

    /// <summary>Market trades in a ChannelSink: a queue where every message counts. One consumer reads it.</summary>
    public static async Task AggTradeAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // When the consumer falls behind, new trades are dropped and an Overflow item says so.
        ChannelSink<AggTradeModel> sink = new(capacity: 10_000, client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.WsStreams.AggTrade.CreateSession(sink.Handler);

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
                            Console.WriteLine($"{item.Model.Symbol}: {item.Model.Quantity} at {item.Model.Price}");
                            break;

                        case StreamItemKind.Reconnected:
                        case StreamItemKind.Overflow:
                            // Trades inside the gap are lost. Rebuild anything made from them, e.g. volume.
                            Console.WriteLine($"Lost trades from {item.Gap.StartUtcMillis} to {item.Gap.EndUtcMillis}");
                            break;

                        default:
                            Console.WriteLine($"{item.Kind}");
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
            Console.WriteLine($"Stream stopped: {exception.Disconnect.Reason}");
        }
    }

    /// <summary>Local order book kept in sync by OrderBookSet. Use one OrderBookSet per depth session.</summary>
    public static async Task DepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // Spot snapshots take 1 to 5000 levels: 1000 costs 50 weight.
        OrderBookSetOptions options = new() { SnapshotLimit = 1000 };

        OrderBookSet<NoOpTelemetryHandler> books = new(client, BinanceMarkets.Spot, options);
        BinanceStreamSession session = client.Spot.WsStreams.Depth.CreateSession(books.Handler, SpotDepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

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
    /// Top 20 bids and asks, sent whole on every push: no snapshot or sync needed. Kept in a LatestCache.
    /// Spot sends no symbol here: the subscription handle tells which one it is.
    /// </summary>
    public static async Task PartialDepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<SpotPartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.WsStreams.PartialDepth.CreateSession(cache.Handler, PartialDepthLevels.Twenty, SpotDepthUpdateSpeed.Ms100);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out SpotPartialDepthModel depth, out _))
            {
                PrintTopLevels(in depth);
            }
        }
    }

    /// <summary>Only the first BidCount and AskCount levels are set.</summary>
    private static void PrintTopLevels(in SpotPartialDepthModel depth)
    {
        ReadOnlySpan<DepthLevel> bids = depth.Bids[..depth.BidCount];
        ReadOnlySpan<DepthLevel> asks = depth.Asks[..depth.AskCount];

        if (bids.IsEmpty || asks.IsEmpty)
        {
            return;
        }

        Console.WriteLine($"{bids.Length} bids from {bids[0].Price}, {asks.Length} asks from {asks[0].Price}");
    }
}
