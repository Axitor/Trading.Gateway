using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Spot;

/// <summary>
/// Spot SBE streams: binary market data, smaller and faster to read than JSON. Times are in microseconds.
/// Needs Spot in BinanceOptions.StreamMarkets and an Ed25519 API key in BinanceSecret.Ed25519ApiKey, see ClientSetupSamples.CreateWithOptions.
/// Without them, CreateSession throws InvalidOperationException.
/// </summary>
internal static class SpotSbeStreamSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Every market trade, one by one, in a ChannelSink.</summary>
    public static async Task TradeAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        // When the consumer falls behind, new trades are dropped and an Overflow item says so.
        ChannelSink<SbeTradeModel> sink = new(capacity: 10_000, client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.Trade.CreateSession(sink.Handler);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        // No handle: rejected or no room. Unconfirmed still has a handle: data comes once the exchange applies it.
        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        try
        {
            while (await sink.WaitToReadAsync(appToken).ConfigureAwait(false))
            {
                while (sink.TryRead(out StreamItem<SbeTradeModel> item))
                {
                    switch (item.Kind)
                    {
                        case StreamItemKind.Data:
                            Console.WriteLine($"{item.Model.Symbol} trade {item.Model.TradeId}: {item.Model.Quantity} at {item.Model.Price}");
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

    /// <summary>Best bid and ask, kept in a LatestCache. Under high load the exchange skips stale updates and sends the latest.</summary>
    public static async Task BestBidAskAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<SbeBestBidAskModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.BestBidAsk.CreateSession(cache.Handler);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        // Read any time, from any thread. False before the first message and during a connection break.
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out SbeBestBidAskModel quote, out _))
            {
                Console.WriteLine($"{quote.Symbol}: bid {quote.BidPrice}, ask {quote.AskPrice}, at {quote.EventTimeMicros} us");
            }
        }
    }

    /// <summary>Local order book kept in sync by OrderBookSet, updated every 20 ms. Snapshots come from the spot REST API.</summary>
    public static async Task DepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        OrderBookSet<NoOpTelemetryHandler> books = new(client, BinanceMarkets.Spot);
        BinanceStreamSession session = client.Spot.SbeStreams.Depth.CreateSession(books.Handler);

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

    /// <summary>Top 20 bids and asks every 50 ms, sent whole on every push: no snapshot or sync needed. Kept in a LatestCache.</summary>
    public static async Task PartialDepthAsync(BinanceClient<NoOpTelemetryHandler> client, CancellationToken appToken)
    {
        LatestCache<SbePartialDepthModel> cache = new(client.StreamSessionCapacity);
        BinanceStreamSession session = client.Spot.SbeStreams.PartialDepth.CreateSession(cache.Handler);

        WsStreamSubscribeResult subscription = await session.SubscribeAsync(_symbol, appToken).ConfigureAwait(false);

        if (subscription.Handle < 0)
        {
            Console.WriteLine($"Subscribe failed: {subscription.Status}");
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), appToken).ConfigureAwait(false);

            if (cache.TryRead(subscription.Handle, out SbePartialDepthModel depth, out _))
            {
                PrintTopLevels(in depth);
            }
        }
    }

    /// <summary>Only the first BidCount and AskCount levels are set.</summary>
    private static void PrintTopLevels(in SbePartialDepthModel depth)
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
