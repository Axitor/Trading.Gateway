using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Streams;

/// <summary>Best bid and ask of one spot symbol. Spot sends no event or transaction time.</summary>
// PERF: Pack = 8 keeps the 16-byte aligned Symbol from rounding the struct up to 64 bytes.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct SpotBookTickerModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    public Symbol Symbol;

    /// <summary>Order book update id. Grows with every update.</summary>
    public long UpdateId;

    /// <summary>Best bid price.</summary>
    public double BidPrice;

    /// <summary>Best bid quantity.</summary>
    public double BidQuantity;

    /// <summary>Best ask price.</summary>
    public double AskPrice;

    /// <summary>Best ask quantity.</summary>
    public double AskQuantity;
}
