using Axitor.Trading.Gateway.Benchmarks.Clients;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients.UsdFuturesApi;
using Binance.Net.SymbolOrderBooks;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Binance.Net on one stream type of the recorded symbols: standard subscriptions, high-performance ones, or its local order books.</summary>
internal static class BinanceNetStreamRunner
{
    private const int DepthIntervalMillis = 100;
    private const int PartialDepthLevelCount = 20;

    // CryptoExchange.Net reads a time number by size: below 2e10 as seconds, below 2e13 as milliseconds, below 2e16 as microseconds.
    // The stub writes microseconds of uptime, so the unit follows the uptime. Each branch converts exactly, so the number comes back whole.
    // A run that crosses a threshold gives wrong latencies: run it again.
    private static readonly long _ticksPerUnit = SelectTicksPerUnit(LatencyCollector.NowMicros());

    /// <summary>Creates the clients and subscribes every symbol. Dispose the result to stop.</summary>
    public static async Task<IAsyncDisposable> StartAsync(StreamKind kind, bool highPerformance, LatencyCollector collector)
    {
        BinanceSocketClient socket = BenchmarkClients.CreateBinanceNetSocketClient();
        BinanceRestClient? rest = null;

        try
        {
            BinanceFuturesUsdtSymbolOrderBook[]? books = null;

            if (kind == StreamKind.Book)
            {
                rest = BenchmarkClients.CreateBinanceNetRestClient();
                books = await StartBooksAsync(socket, rest, collector).ConfigureAwait(false);
            }
            else if (highPerformance)
            {
                await SubscribeHighPerformanceAsync(socket.UsdFuturesApi.ExchangeData, kind, collector).ConfigureAwait(false);
            }
            else
            {
                await SubscribeAsync(socket.UsdFuturesApi.ExchangeData, kind, collector).ConfigureAwait(false);
            }

            return new Run(socket, rest, books);
        }
        catch
        {
            socket.Dispose();
            rest?.Dispose();
            throw;
        }
    }

    private static async Task SubscribeAsync(IBinanceSocketClientUsdFuturesApiExchangeData api, StreamKind kind, LatencyCollector collector)
    {
        string[] symbols = BenchmarkClients.StreamSymbolNames;

        WebSocketResult<UpdateSubscription> result = kind switch
        {
            StreamKind.BookTicker => await api.SubscribeToBookTickerUpdatesAsync(
                symbols, update => collector.Record(ToMicros(update.DataTime))).ConfigureAwait(false),
            StreamKind.AggTrade => await api.SubscribeToAggregatedTradeUpdatesAsync(
                symbols, update => collector.Record(ToMicros(update.DataTime))).ConfigureAwait(false),
            StreamKind.Depth => await api.SubscribeToOrderBookUpdatesAsync(
                symbols, DepthIntervalMillis, update => collector.Record(ToMicros(update.DataTime))).ConfigureAwait(false),
            StreamKind.Depth20 => await api.SubscribeToPartialOrderBookUpdatesAsync(
                symbols, PartialDepthLevelCount, DepthIntervalMillis, update => collector.Record(ToMicros(update.DataTime))).ConfigureAwait(false),
            StreamKind.Kline => await api.SubscribeToKlineUpdatesAsync(
                symbols, KlineInterval.OneMinute, update => collector.Record(ToMicros(update.DataTime))).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        if (!result.Success)
        {
            throw new InvalidOperationException($"Binance.Net {kind}: {result.Error}. Is the stub running on {BenchmarkClients.StubHost}?");
        }

        result.Data!.ConnectionLost += collector.AddGap;
    }

    /// <summary>The high-performance models carry no event time, so these runs count messages only.</summary>
    private static async Task SubscribeHighPerformanceAsync(IBinanceSocketClientUsdFuturesApiExchangeData api, StreamKind kind, LatencyCollector collector)
    {
        string[] symbols = BenchmarkClients.StreamSymbolNames;

        WebSocketResult<HighPerfUpdateSubscription> result = kind switch
        {
            StreamKind.BookTicker => await api.SubscribeToBookTickerUpdatesPerfAsync(
                symbols, _ => collector.Count(), CancellationToken.None).ConfigureAwait(false),
            StreamKind.AggTrade => await api.SubscribeToAggregatedTradeUpdatesPerfAsync(
                symbols, _ => collector.Count(), CancellationToken.None).ConfigureAwait(false),
            StreamKind.Depth => await api.SubscribeToOrderBookUpdatesPerfAsync(
                symbols, DepthIntervalMillis, _ => collector.Count(), CancellationToken.None).ConfigureAwait(false),
            StreamKind.Depth20 => await api.SubscribeToPartialOrderBookUpdatesPerfAsync(
                symbols, PartialDepthLevelCount, DepthIntervalMillis, _ => collector.Count(), CancellationToken.None).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        if (!result.Success)
        {
            throw new InvalidOperationException($"Binance.Net high-performance {kind}: {result.Error}. Is the stub running on {BenchmarkClients.StubHost}?");
        }

        result.Data!.ConnectionClosed += collector.AddGap;
    }

    private static async Task<BinanceFuturesUsdtSymbolOrderBook[]> StartBooksAsync(BinanceSocketClient socket, BinanceRestClient rest, LatencyCollector collector)
    {
        string[] symbols = BenchmarkClients.StreamSymbolNames;
        BinanceFuturesUsdtSymbolOrderBook[] books = new BinanceFuturesUsdtSymbolOrderBook[symbols.Length];
        Task<CallResult>[] starts = new Task<CallResult>[symbols.Length];

        for (int i = 0; i < symbols.Length; i++)
        {
            books[i] = new BinanceFuturesUsdtSymbolOrderBook(symbols[i], options => options.UpdateInterval = DepthIntervalMillis, null, rest, socket);
            Attach(books[i], collector);
        }

        // All at once: the stub starts its pass once subscriptions stop changing for half a second.
        for (int i = 0; i < books.Length; i++)
        {
            starts[i] = books[i].StartAsync();
        }

        CallResult[] results = await Task.WhenAll(starts).ConfigureAwait(false);

        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i].Success)
            {
                foreach (BinanceFuturesUsdtSymbolOrderBook book in books)
                {
                    book.Dispose();
                }

                throw new InvalidOperationException($"Binance.Net order book {symbols[i]}: {results[i].Error}. Is the stub running on {BenchmarkClients.StubHost}?");
            }
        }

        return books;
    }

    private static void Attach(BinanceFuturesUsdtSymbolOrderBook book, LatencyCollector collector)
    {
        long lastSequence = 0;

        // Fires after every queued item, skipped ones included: only a moved sequence number means the update was applied.
        // A REST snapshot moves it too but has no server time, so it is not counted.
        book.OnOrderBookUpdate += _ =>
        {
            long sequence = book.LastSequenceNumber;

            if (sequence == lastSequence)
            {
                return;
            }

            lastSequence = sequence;

            if (book.UpdateServerTime is { } serverTime)
            {
                collector.Record(ToMicros(serverTime));
            }
        };

        book.OnStatusChange += (previous, current) =>
        {
            if (previous == OrderBookStatus.Synced)
            {
                collector.AddResync();
            }
        };
    }

    private static long ToMicros(DateTime? time) =>
        time is { } value ? (value.Ticks - DateTime.UnixEpoch.Ticks) / _ticksPerUnit : 0;

    private static long SelectTicksPerUnit(long micros) =>
        micros < 19_999_999_999 ? TimeSpan.TicksPerSecond
        : micros < 19_999_999_999_999 ? TimeSpan.TicksPerMillisecond
        : TimeSpan.TicksPerMicrosecond;

    private sealed class Run : IAsyncDisposable
    {
        private readonly BinanceSocketClient _socket;
        private readonly BinanceRestClient? _rest;
        private readonly BinanceFuturesUsdtSymbolOrderBook[]? _books;

        public Run(BinanceSocketClient socket, BinanceRestClient? rest, BinanceFuturesUsdtSymbolOrderBook[]? books)
        {
            _socket = socket;
            _rest = rest;
            _books = books;
        }

        public async ValueTask DisposeAsync()
        {
            if (_books is not null)
            {
                foreach (BinanceFuturesUsdtSymbolOrderBook book in _books)
                {
                    await book.StopAsync().ConfigureAwait(false);
                    book.Dispose();
                }
            }

            _socket.Dispose();
            _rest?.Dispose();
        }
    }
}
