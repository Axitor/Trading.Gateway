using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Our client on one stream type of the recorded symbols.</summary>
internal static class AxitorStreamRunner
{
    /// <summary>Creates the client and subscribes every symbol. Dispose the result to stop.</summary>
    public static async Task<IAsyncDisposable> StartAsync(StreamKind kind, LatencyCollector collector)
    {
        BinanceClient<NoOpTelemetryHandler> client = new(BenchmarkClients.CreateAxitorOptions(BinanceMarkets.FuturesUsdM), new EngineOptions());

        try
        {
            WsStreams<NoOpTelemetryHandler> streams = client.Futures.UsdM.WsStreams;

            BinanceStreamSession session = kind switch
            {
                StreamKind.BookTicker => streams.BookTicker.CreateSession(new BookTickerHandler(collector)),
                StreamKind.AggTrade => streams.AggTrade.CreateSession(new AggTradeHandler(collector)),
                StreamKind.Depth => streams.Depth.CreateSession(new DepthHandler(collector), DepthUpdateSpeed.Ms100),
                StreamKind.Depth20 => streams.PartialDepth.CreateSession(new PartialDepthHandler(collector), PartialDepthLevels.Twenty, DepthUpdateSpeed.Ms100),
                StreamKind.Kline => streams.Kline.CreateSession(new KlineHandler(collector), BinanceInterval.OneMinute),
                _ => streams.Depth.CreateSession(
                    new BookHandler(new OrderBookSet<NoOpTelemetryHandler>(client, BinanceMarkets.FuturesUsdM), collector),
                    DepthUpdateSpeed.Ms100),
            };

            foreach (string name in BenchmarkClients.StreamSymbolNames)
            {
                WsStreamSubscribeResult result = await session.SubscribeAsync(Symbol.Parse(name)).ConfigureAwait(false);

                if (result.Status != WsStreamSubscribeStatus.Subscribed)
                {
                    throw new InvalidOperationException($"Axitor {kind} {name}: {result.Status}. Is the stub running on {BenchmarkClients.StubHost}?");
                }
            }

            return new Run(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly BinanceClient<NoOpTelemetryHandler> _client;

        public Run(BinanceClient<NoOpTelemetryHandler> client) => _client = client;

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private readonly struct BookTickerHandler : IBinanceStreamHandler<BookTickerModel>
    {
        private readonly LatencyCollector _collector;

        public BookTickerHandler(LatencyCollector collector) => _collector = collector;

        public void OnData(int subscription, in BookTickerModel model) => _collector.Record(model.EventTime);

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _collector.AddGap();

        public void OnReconnected(int subscription, in WsStreamGap gap) { }

        public void OnUnsubscribed(int subscription) { }
    }

    private readonly struct AggTradeHandler : IBinanceStreamHandler<AggTradeModel>
    {
        private readonly LatencyCollector _collector;

        public AggTradeHandler(LatencyCollector collector) => _collector = collector;

        public void OnData(int subscription, in AggTradeModel model) => _collector.Record(model.EventTime);

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _collector.AddGap();

        public void OnReconnected(int subscription, in WsStreamGap gap) { }

        public void OnUnsubscribed(int subscription) { }
    }

    private readonly struct PartialDepthHandler : IBinanceStreamHandler<PartialDepthModel>
    {
        private readonly LatencyCollector _collector;

        public PartialDepthHandler(LatencyCollector collector) => _collector = collector;

        public void OnData(int subscription, in PartialDepthModel model) => _collector.Record(model.EventTime);

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _collector.AddGap();

        public void OnReconnected(int subscription, in WsStreamGap gap) { }

        public void OnUnsubscribed(int subscription) { }
    }

    private readonly struct KlineHandler : IBinanceStreamHandler<KlineUpdateModel>
    {
        private readonly LatencyCollector _collector;

        public KlineHandler(LatencyCollector collector) => _collector = collector;

        public void OnData(int subscription, in KlineUpdateModel model) => _collector.Record(model.EventTime);

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _collector.AddGap();

        public void OnReconnected(int subscription, in WsStreamGap gap) { }

        public void OnUnsubscribed(int subscription) { }
    }

    private readonly struct DepthHandler : IBinanceDepthHandler
    {
        private readonly LatencyCollector _collector;

        public DepthHandler(LatencyCollector collector) => _collector = collector;

        public void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks) =>
            _collector.Record(header.EventTime);

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _collector.AddGap();

        public void OnReconnected(int subscription, in WsStreamGap gap) { }

        public void OnUnsubscribed(int subscription) { }
    }

    /// <summary>Passes updates to the order book set and samples the ones it applied.</summary>
    private readonly struct BookHandler : IBinanceDepthHandler
    {
        private readonly OrderBookSet<NoOpTelemetryHandler> _set;
        private readonly OrderBookSetHandler<NoOpTelemetryHandler> _inner;
        private readonly LatencyCollector _collector;

        public BookHandler(OrderBookSet<NoOpTelemetryHandler> set, LatencyCollector collector)
        {
            _set = set;
            _inner = set.Handler;
            _collector = collector;
        }

        public void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
        {
            bool wasSynced = _set.IsSynced(subscription);
            long version = _set.GetVersion(subscription);

            _inner.OnData(subscription, in header, bids, asks);

            if (!_set.IsSynced(subscription))
            {
                if (wasSynced)
                {
                    _collector.AddResync();
                }

                return;
            }

            // Updates older than the snapshot are dropped without a change.
            if (_set.GetVersion(subscription) != version)
            {
                _collector.Record(header.EventTime);
            }
        }

        public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal)
        {
            _inner.OnDisconnected(subscription, reason, isFatal);
            _collector.AddGap();
        }

        public void OnReconnected(int subscription, in WsStreamGap gap) => _inner.OnReconnected(subscription, in gap);

        public void OnUnsubscribed(int subscription) => _inner.OnUnsubscribed(subscription);
    }
}
