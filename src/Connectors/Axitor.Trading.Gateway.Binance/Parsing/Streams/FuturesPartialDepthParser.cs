using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Partial depth parser of USD-M and COIN-M.</summary>
internal readonly struct FuturesPartialDepthParser : IBinanceStreamParser<PartialDepthModel>
{
    // The event type always comes first: skipping it keeps the next read on the fast path.
    private static ReadOnlySpan<byte> EventPrefix => "{\"e\":\"depthUpdate\""u8;

    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;
    private static ReadOnlySpan<byte> TransactionTimeKey => "\"T\":"u8;
    private static ReadOnlySpan<byte> SymbolKey => "\"s\":"u8;
    private static ReadOnlySpan<byte> PairKey => "\"ps\":"u8;
    private static ReadOnlySpan<byte> FirstUpdateIdKey => "\"U\":"u8;
    private static ReadOnlySpan<byte> FinalUpdateIdKey => "\"u\":"u8;
    private static ReadOnlySpan<byte> PreviousFinalUpdateIdKey => "\"pu\":"u8;
    private static ReadOnlySpan<byte> BidsKey => "\"b\":"u8;
    private static ReadOnlySpan<byte> AsksKey => "\"a\":"u8;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out PartialDepthModel model, out long updateId)
    {
        model = default;
        updateId = 0;
        int cursor = data.StartsWith(EventPrefix) ? EventPrefix.Length : BinanceUtf8Reader.ObjectStart;

        if (!BinanceUtf8Reader.TryReadInt64(data, EventTimeKey, ref cursor, out model.EventTime)
            || !BinanceUtf8Reader.TryReadInt64(data, TransactionTimeKey, ref cursor, out model.TransactionTime)
            || !BinanceUtf8Reader.TryReadSymbol(data, SymbolKey, ref cursor, out model.Symbol))
        {
            return false;
        }

        // U and pu are skipped: every message is the whole top, nothing chains to the previous one.
        BinanceUtf8Reader.SkipOptional(data, PairKey, ref cursor);
        BinanceUtf8Reader.SkipOptional(data, FirstUpdateIdKey, ref cursor);

        if (!BinanceUtf8Reader.TryReadInt64(data, FinalUpdateIdKey, ref cursor, out model.FinalUpdateId))
        {
            return false;
        }

        BinanceUtf8Reader.SkipOptional(data, PreviousFinalUpdateIdKey, ref cursor);

        if (!BinanceDepthReader.TryReadLevels(data, BidsKey, ref cursor, model.Bids, out model.BidCount)
            || !BinanceDepthReader.TryReadLevels(data, AsksKey, ref cursor, model.Asks, out model.AskCount))
        {
            return false;
        }

        updateId = model.FinalUpdateId;
        return true;
    }
}
