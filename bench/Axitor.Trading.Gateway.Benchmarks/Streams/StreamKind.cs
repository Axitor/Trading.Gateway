namespace Axitor.Trading.Gateway.Benchmarks.Streams;

/// <summary>Stream type of a harness run.</summary>
internal enum StreamKind
{
    BookTicker,

    AggTrade,

    /// <summary>Diff depth at 100 ms.</summary>
    Depth,

    /// <summary>Top 20 levels at 100 ms.</summary>
    Depth20,

    /// <summary>One-minute klines.</summary>
    Kline,

    /// <summary>Local order books built from diff depth at 100 ms.</summary>
    Book,
}
