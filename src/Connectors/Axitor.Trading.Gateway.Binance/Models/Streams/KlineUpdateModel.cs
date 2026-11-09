using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Streams;

/// <summary>Current candle of one futures symbol. Each message holds the whole candle and replaces the previous one.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from padding the struct to a 16-byte multiple.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct KlineUpdateModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Event time, milliseconds since the Unix epoch.</summary>
    public long EventTime;

    /// <summary>First trade id in the candle.</summary>
    public long FirstTradeId;

    /// <summary>Last trade id in the candle.</summary>
    public long LastTradeId;

    /// <summary>The candle so far; final once IsClosed is true.</summary>
    public Kline Kline;

    /// <summary>Whether the candle has closed: this is its final state.</summary>
    public bool IsClosed;
}
