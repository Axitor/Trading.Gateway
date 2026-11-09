using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Kline parser of USD-M.</summary>
/// <remarks>
/// DESIGN NOTE: Klines have no growing update id, so duplicates are dropped by event time.
/// The last trade id cannot serve: the closing push of a candle may carry the same one as the push before it.
/// </remarks>
internal readonly struct FuturesKlineParser : IBinanceStreamParser<KlineUpdateModel>
{
    // The event type always comes first: skipping it keeps the next read on the fast path.
    private static ReadOnlySpan<byte> EventPrefix => "{\"e\":\"kline\""u8;

    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;
    private static ReadOnlySpan<byte> SymbolKey => "\"s\":"u8;
    private static ReadOnlySpan<byte> KlineKey => "\"k\":"u8;
    private static ReadOnlySpan<byte> OpenTimeKey => "\"t\":"u8;
    private static ReadOnlySpan<byte> CloseTimeKey => "\"T\":"u8;
    private static ReadOnlySpan<byte> IntervalKey => "\"i\":"u8;
    private static ReadOnlySpan<byte> FirstTradeIdKey => "\"f\":"u8;
    private static ReadOnlySpan<byte> LastTradeIdKey => "\"L\":"u8;
    private static ReadOnlySpan<byte> OpenKey => "\"o\":"u8;
    private static ReadOnlySpan<byte> CloseKey => "\"c\":"u8;
    private static ReadOnlySpan<byte> HighKey => "\"h\":"u8;
    private static ReadOnlySpan<byte> LowKey => "\"l\":"u8;
    private static ReadOnlySpan<byte> VolumeKey => "\"v\":"u8;
    private static ReadOnlySpan<byte> TradeCountKey => "\"n\":"u8;
    private static ReadOnlySpan<byte> IsClosedKey => "\"x\":"u8;
    private static ReadOnlySpan<byte> QuoteVolumeKey => "\"q\":"u8;
    private static ReadOnlySpan<byte> TakerBuyVolumeKey => "\"V\":"u8;
    private static ReadOnlySpan<byte> TakerBuyQuoteVolumeKey => "\"Q\":"u8;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out KlineUpdateModel model, out long updateId)
    {
        model = default;
        updateId = 0;
        int cursor = data.StartsWith(EventPrefix) ? EventPrefix.Length : BinanceUtf8Reader.ObjectStart;

        if (!BinanceUtf8Reader.TryReadInt64(data, EventTimeKey, ref cursor, out model.EventTime)
            || !BinanceUtf8Reader.TryReadSymbol(data, SymbolKey, ref cursor, out model.Symbol)
            || !BinanceUtf8Reader.TrySeekValue(data, KlineKey, ref cursor)
            || (uint)data.Length <= (uint)cursor
            || data[cursor] != (byte)'{')
        {
            return false;
        }

        // The first key of the candle follows its opening brace with no comma.
        cursor++;

        if (!BinanceUtf8Reader.TryReadInt64(data, OpenTimeKey, ref cursor, out model.Kline.OpenTime)
            || !BinanceUtf8Reader.TryReadInt64(data, CloseTimeKey, ref cursor, out model.Kline.CloseTime))
        {
            return false;
        }

        // The symbol repeats the outer one; the interval is the session's.
        BinanceUtf8Reader.SkipOptional(data, SymbolKey, ref cursor);
        BinanceUtf8Reader.SkipOptional(data, IntervalKey, ref cursor);

        // In the order the exchange writes them, so the normal case is one pass. The trailing B is never reached.
        if (!BinanceUtf8Reader.TryReadInt64(data, FirstTradeIdKey, ref cursor, out model.FirstTradeId)
            || !BinanceUtf8Reader.TryReadInt64(data, LastTradeIdKey, ref cursor, out model.LastTradeId)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, OpenKey, ref cursor, out model.Kline.Open)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, CloseKey, ref cursor, out model.Kline.Close)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, HighKey, ref cursor, out model.Kline.High)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, LowKey, ref cursor, out model.Kline.Low)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, VolumeKey, ref cursor, out model.Kline.Volume)
            || !BinanceUtf8Reader.TryReadInt64(data, TradeCountKey, ref cursor, out long tradeCount)
            || (ulong)tradeCount > int.MaxValue
            || !BinanceUtf8Reader.TryReadBoolean(data, IsClosedKey, ref cursor, out model.IsClosed)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, QuoteVolumeKey, ref cursor, out model.Kline.QuoteVolume)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, TakerBuyVolumeKey, ref cursor, out model.Kline.TakerBuyVolume)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, TakerBuyQuoteVolumeKey, ref cursor, out model.Kline.TakerBuyQuoteVolume))
        {
            return false;
        }

        model.Kline.TradeCount = (int)tradeCount;

        // HOT: branchless; the closed flag is the lowest bit.
        updateId = (model.EventTime << 1) | (model.IsClosed ? 1L : 0L);
        return true;
    }
}
