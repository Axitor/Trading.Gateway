using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;

namespace Axitor.Trading.Gateway.Binance.Parsing.Sbe;

/// <summary>DepthDiffStreamEvent parser. Feeds the same depth handlers and order books as the spot JSON depth stream.</summary>
/// <remarks>
/// DESIGN NOTE: The shared header keeps event time in milliseconds: the order book does not use it, and the stream pushes every 20 ms.
/// </remarks>
internal readonly struct SbeDepthUpdateParser : IBinanceDepthParser
{
    private const long MicrosPerMillisecond = 1000;

    // Root block of version 0.
    private const int RootLength = 26;
    private const int EventTimeOffset = 0;
    private const int FirstUpdateIdOffset = 8;
    private const int FinalUpdateIdOffset = 16;
    private const int PriceExponentOffset = 24;
    private const int QuantityExponentOffset = 25;

    /// <inheritdoc />
    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out DepthUpdateHeader header,
        ref DepthLevel[] bids,
        out int bidCount,
        ref DepthLevel[] asks,
        out int askCount)
    {
        header = default;
        bidCount = 0;
        askCount = 0;

        if (!BinanceSbeReader.TryReadHeader(data, out _, out int blockLength) || blockLength < RootLength)
        {
            return false;
        }

        int root = BinanceSbeReader.HeaderLength;
        int cursor = root + blockLength;
        int priceExponent = BinanceSbeReader.ReadInt8(data, root + PriceExponentOffset);
        int quantityExponent = BinanceSbeReader.ReadInt8(data, root + QuantityExponentOffset);

        if (!BinanceSbeReader.TryReadLevels(data, ref cursor, priceExponent, quantityExponent, ref bids, out bidCount)
            || !BinanceSbeReader.TryReadLevels(data, ref cursor, priceExponent, quantityExponent, ref asks, out askCount)
            || !BinanceSbeReader.TryReadSymbol(data, ref cursor, out header.Symbol))
        {
            bidCount = 0;
            askCount = 0;
            return false;
        }

        header.EventTime = BinanceSbeReader.ReadInt64(data, root + EventTimeOffset) / MicrosPerMillisecond;
        header.FirstUpdateId = BinanceSbeReader.ReadInt64(data, root + FirstUpdateIdOffset);
        header.FinalUpdateId = BinanceSbeReader.ReadInt64(data, root + FinalUpdateIdOffset);

        // Spot chains by U = previous u + 1, which is the same check against U - 1.
        header.PreviousFinalUpdateId = header.FirstUpdateId - 1;
        return true;
    }
}
