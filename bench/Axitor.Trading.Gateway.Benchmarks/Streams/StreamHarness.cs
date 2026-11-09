using System.Diagnostics;
using System.Globalization;
using Axitor.Trading.Gateway.Benchmarks.Clients;

namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Runs one client on one stream type over the stub's replay and prints the result as a markdown table row.</summary>
/// <remarks>
/// <para>Start the stub with pace 1 for latency or max for throughput. The stub replays once per connection.</para>
/// <para>
/// With a warmup pass the client takes a whole pass first and closes; a new client in the same warm process then
/// measures a fresh pass from its first message. Without one, the warmup is the start of the measured pass.
/// </para>
/// <para>
/// The sample array lives on the large object heap. It is allocated before the warmup and a full collection runs after both,
/// so the collection its allocation would trigger never falls inside the measured window.
/// </para>
/// </remarks>
internal static class StreamHarness
{
    private const string AxitorClient = "axitor";
    private const string BinanceNetClient = "binance-net";
    private const string BinanceNetPerfClient = "binance-net-perf";
    private const string RawClient = "raw";
    private const string WarmupPassOption = "--warmup-pass";
    private const string WarmupMessagesOption = "--warmup-msgs";
    private const string WindowOption = "--window";
    private const int DefaultWarmupMessages = 50_000;
    private const int SampleCapacity = 4 * 1024 * 1024;

    // Samples of the warmup pass are dropped, so one slot is enough.
    private const int WarmupSampleCapacity = 1;

    private static readonly TimeSpan _warmupTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _endSilence = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _firstMessageTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Arguments: client, stream, then --warmup-pass, --warmup-msgs N, --window seconds (0 to the end of the pass). Returns the exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParseArguments(args, out Arguments arguments))
        {
            PrintUsage();
            return 1;
        }

        if (!IsSupported(arguments.Client, arguments.Kind))
        {
            Console.Error.WriteLine("binance-net-perf has no kline stream or order book; raw has no order book.");
            return 1;
        }

        LatencyCollector collector = new(SampleCapacity);

        if (arguments.WarmupPass)
        {
            await RunWarmupPassAsync(arguments.Client, arguments.Kind).ConfigureAwait(false);
        }

        CollectGarbage();

        IAsyncDisposable run = await StartAsync(arguments.Client, arguments.Kind, collector).ConfigureAwait(false);

        Console.WriteLine($"{arguments.Client}, {NameOf(arguments.Kind)}: {BenchmarkClients.StreamSymbolNames.Length} symbols subscribed, waiting for the stub's pass.");

        Window? window;

        try
        {
            window = await MeasureAsync(collector, arguments.WarmupMessages, arguments.Window, fromFirstMessage: arguments.WarmupPass).ConfigureAwait(false);
        }
        finally
        {
            await run.DisposeAsync().ConfigureAwait(false);
        }

        if (window is not { } measured || measured.End.Received == measured.Start.Received)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Nothing measured: the pass ended inside the warmup or never synced. Received {collector.Received}, gaps {collector.Gaps}, resyncs {collector.Resyncs}."));
            return 1;
        }

        PrintReport(arguments, collector, measured);
        return 0;
    }

    private static bool IsSupported(string client, StreamKind kind) =>
        client switch
        {
            BinanceNetPerfClient => kind is not (StreamKind.Kline or StreamKind.Book),
            RawClient => kind != StreamKind.Book,
            _ => true,
        };

    private static Task<IAsyncDisposable> StartAsync(string client, StreamKind kind, LatencyCollector collector) =>
        client switch
        {
            AxitorClient => AxitorStreamRunner.StartAsync(kind, collector),
            BinanceNetClient => BinanceNetStreamRunner.StartAsync(kind, highPerformance: false, collector),
            BinanceNetPerfClient => BinanceNetStreamRunner.StartAsync(kind, highPerformance: true, collector),
            _ => RawStreamRunner.StartAsync(kind, collector),
        };

    /// <summary>One whole pass with the same code as the measured one.</summary>
    private static async Task RunWarmupPassAsync(string client, StreamKind kind)
    {
        LatencyCollector collector = new(WarmupSampleCapacity);
        IAsyncDisposable run = await StartAsync(client, kind, collector).ConfigureAwait(false);

        try
        {
            await MeasureAsync(collector, 0, TimeSpan.Zero, fromFirstMessage: true).ConfigureAwait(false);
        }
        finally
        {
            await run.DisposeAsync().ConfigureAwait(false);
        }

        Console.WriteLine($"Warmup pass: {collector.Received} msgs.");
    }

    /// <summary>A full blocking collection, so the measured run starts on a clean heap with fresh budgets.</summary>
    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Waits out the warmup, then follows the pass until it goes silent or the window is full. Null when the pass ended inside the warmup.</summary>
    private static async Task<Window?> MeasureAsync(LatencyCollector collector, int warmupMessages, TimeSpan windowLimit, bool fromFirstMessage)
    {
        using PeriodicTimer timer = new(_pollInterval);
        long waitStart = Stopwatch.GetTimestamp();
        Snapshot start = default;
        Snapshot end = default;
        bool isMeasuring = false;

        if (fromFirstMessage)
        {
            start = Snapshot.Take(collector, Stopwatch.GetTimestamp());
            collector.StartSampling();
            end = start;
            isMeasuring = true;
        }

        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            long received = collector.Received;

            if (received == 0)
            {
                if (_firstMessageTimeout < Stopwatch.GetElapsedTime(waitStart))
                {
                    throw new TimeoutException($"No data in {_firstMessageTimeout.TotalSeconds:F0} s. Is the stub running on {BenchmarkClients.StubHost}?");
                }

                continue;
            }

            bool isSilent = _endSilence <= Stopwatch.GetElapsedTime(collector.LastTicks);

            if (!isMeasuring)
            {
                if (received < warmupMessages && Stopwatch.GetElapsedTime(collector.FirstTicks) < _warmupTime)
                {
                    if (isSilent)
                    {
                        return null;
                    }

                    continue;
                }

                // The window opens before sampling starts, so no sampled message falls outside it.
                start = Snapshot.Take(collector, Stopwatch.GetTimestamp());
                collector.StartSampling();
                end = start;
                isMeasuring = true;
                continue;
            }

            if (TimeSpan.Zero < windowLimit && windowLimit <= Stopwatch.GetElapsedTime(start.Ticks))
            {
                return new Window(start, Snapshot.Take(collector, Stopwatch.GetTimestamp()));
            }

            if (received != end.Received)
            {
                end = Snapshot.Take(collector, collector.LastTicks);
                continue;
            }

            if (isSilent)
            {
                // Before the clients stop: closing them would count as gaps.
                return new Window(start, end with { Gaps = collector.Gaps, Resyncs = collector.Resyncs });
            }
        }

        return null;
    }

    private static void PrintReport(Arguments arguments, LatencyCollector collector, Window window)
    {
        Snapshot start = window.Start;
        Snapshot end = window.End;

        // Measured from the first message: a window opened before the data would count the stub's settle time.
        long startTicks = Math.Max(start.Ticks, collector.FirstTicks);
        long messages = end.Received - start.Received;
        double seconds = (end.Ticks - startTicks) / (double)Stopwatch.Frequency;
        double bytesPerMessage = (end.AllocatedBytes - start.AllocatedBytes) / (double)messages;
        ReadOnlySpan<long> sorted = collector.SortSamples();
        CultureInfo culture = CultureInfo.InvariantCulture;

        string warmup = arguments.WarmupPass
            ? "after a warmup pass, the whole fresh pass"
            : string.Create(culture, $"warmup {start.Received} msgs (limit {arguments.WarmupMessages} or {_warmupTime.TotalSeconds:F0} s)");

        string limit = arguments.Window == TimeSpan.Zero
            ? "to the end of the pass"
            : string.Create(culture, $"limit {arguments.Window.TotalSeconds:F0} s");

        Console.WriteLine();
        Console.WriteLine("| Client | Stream | Msgs | Msg/s | p50 µs | p90 µs | p99 µs | p99.9 µs | Max µs | B/msg | Gen0 | Gen1 | Gen2 | GC pause ms |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        Console.WriteLine(string.Create(
            culture,
            $"| {arguments.Client} | {NameOf(arguments.Kind)} | {messages} | {messages / seconds:F0} | {FormatLatency(sorted)} | {bytesPerMessage:F0} | " +
            $"{end.Gen0 - start.Gen0} | {end.Gen1 - start.Gen1} | {end.Gen2 - start.Gen2} | {(end.Pause - start.Pause).TotalMilliseconds:F1} |"));
        Console.WriteLine(string.Create(
            culture,
            $"Warmup: {warmup}. Window {seconds:F2} s ({limit}), latency samples {sorted.Length}{(collector.IsTruncated ? " (truncated)" : string.Empty)}, " +
            $"gaps {end.Gaps}, resyncs {end.Resyncs}."));
    }

    private static string FormatLatency(ReadOnlySpan<long> sorted)
    {
        if (sorted.IsEmpty)
        {
            return "n/a | n/a | n/a | n/a | n/a";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{LatencyCollector.Percentile(sorted, 0.50)} | {LatencyCollector.Percentile(sorted, 0.90)} | {LatencyCollector.Percentile(sorted, 0.99)} | " +
            $"{LatencyCollector.Percentile(sorted, 0.999)} | {sorted[^1]}");
    }

    private static bool TryParseArguments(string[] args, out Arguments arguments)
    {
        arguments = default;

        if (args.Length < 2 || !IsClient(args[0]) || !TryParseKind(args[1], out StreamKind kind))
        {
            return false;
        }

        bool warmupPass = false;
        int warmupMessages = DefaultWarmupMessages;
        int windowSeconds = 0;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == WarmupPassOption)
            {
                warmupPass = true;
                continue;
            }

            if (i + 1 == args.Length)
            {
                return false;
            }

            if (args[i] == WarmupMessagesOption)
            {
                if (!TryParseCount(args[++i], out warmupMessages))
                {
                    return false;
                }
            }
            else if (args[i] == WindowOption)
            {
                if (!TryParseCount(args[++i], out windowSeconds))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        arguments = new Arguments(args[0], kind, warmupPass, warmupMessages, TimeSpan.FromSeconds(windowSeconds));
        return true;
    }

    private static bool TryParseCount(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool IsClient(string value) => value is AxitorClient or BinanceNetClient or BinanceNetPerfClient or RawClient;

    private static bool TryParseKind(string value, out StreamKind kind)
    {
        switch (value.ToLowerInvariant())
        {
            case "bookticker":
                kind = StreamKind.BookTicker;
                return true;
            case "aggtrade":
                kind = StreamKind.AggTrade;
                return true;
            case "depth":
                kind = StreamKind.Depth;
                return true;
            case "depth20":
                kind = StreamKind.Depth20;
                return true;
            case "kline":
                kind = StreamKind.Kline;
                return true;
            case "book":
                kind = StreamKind.Book;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string NameOf(StreamKind kind) =>
        kind switch
        {
            StreamKind.BookTicker => "bookTicker",
            StreamKind.AggTrade => "aggTrade",
            StreamKind.Depth => "depth@100ms",
            StreamKind.Depth20 => "depth20@100ms",
            StreamKind.Kline => "kline_1m",
            _ => "book (depth@100ms)",
        };

    private static void PrintUsage() =>
        Console.Error.WriteLine(
            "Usage: streams <axitor|binance-net|binance-net-perf|raw> <bookticker|aggtrade|depth|depth20|kline|book> " +
            "[--warmup-pass] [--warmup-msgs N] [--window seconds]");

    private readonly record struct Arguments(string Client, StreamKind Kind, bool WarmupPass, int WarmupMessages, TimeSpan Window);

    private readonly record struct Window(Snapshot Start, Snapshot End);

    private readonly record struct Snapshot(
        long Ticks,
        long Received,
        long AllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        TimeSpan Pause,
        long Gaps,
        long Resyncs)
    {
        public static Snapshot Take(LatencyCollector collector, long ticks) =>
            new(
                ticks,
                collector.Received,
                GC.GetTotalAllocatedBytes(precise: true),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                GC.GetTotalPauseDuration(),
                collector.Gaps,
                collector.Resyncs);
    }
}
