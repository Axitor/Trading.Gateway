using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Diff depth parser of spot.</summary>
internal readonly struct SpotDepthUpdateParser : IBinanceDepthParser
{
    // The event type always comes first: skipping it keeps the next read on the fast path.
    private static ReadOnlySpan<byte> EventPrefix => "{\"e\":\"depthUpdate\""u8;

    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;
    private static ReadOnlySpan<byte> SymbolKey => "\"s\":"u8;
    private static ReadOnlySpan<byte> FirstUpdateIdKey => "\"U\":"u8;
    private static ReadOnlySpan<byte> FinalUpdateIdKey => "\"u\":"u8;
    private static ReadOnlySpan<byte> BidsKey => "\"b\":"u8;
    private static ReadOnlySpan<byte> AsksKey => "\"a\":"u8;

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
        int cursor = data.StartsWith(EventPrefix) ? EventPrefix.Length : BinanceUtf8Reader.ObjectStart;

        // In the order the exchange writes them, so the normal case is one pass.
        if (!BinanceUtf8Reader.TryReadInt64(data, EventTimeKey, ref cursor, out header.EventTime)
            || !BinanceUtf8Reader.TryReadSymbol(data, SymbolKey, ref cursor, out header.Symbol)
            || !BinanceUtf8Reader.TryReadInt64(data, FirstUpdateIdKey, ref cursor, out header.FirstUpdateId)
            || !BinanceUtf8Reader.TryReadInt64(data, FinalUpdateIdKey, ref cursor, out header.FinalUpdateId))
        {
            return false;
        }

        // Spot sends no pu and chains by U = previous u + 1, which is the same check against U - 1.
        header.PreviousFinalUpdateId = header.FirstUpdateId - 1;

        return BinanceDepthReader.TryReadLevels(data, BidsKey, ref cursor, ref bids, out bidCount)
            && BinanceDepthReader.TryReadLevels(data, AsksKey, ref cursor, ref asks, out askCount);
    }
}
