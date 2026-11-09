using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Aggregate trade parser of spot.</summary>
internal readonly struct SpotAggTradeParser : IBinanceStreamParser<AggTradeModel>
{
    // The event type always comes first: skipping it keeps the next read on the fast path.
    private static ReadOnlySpan<byte> EventPrefix => "{\"e\":\"aggTrade\""u8;

    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;
    private static ReadOnlySpan<byte> SymbolKey => "\"s\":"u8;
    private static ReadOnlySpan<byte> AggregateTradeIdKey => "\"a\":"u8;
    private static ReadOnlySpan<byte> PriceKey => "\"p\":"u8;
    private static ReadOnlySpan<byte> QuantityKey => "\"q\":"u8;
    private static ReadOnlySpan<byte> FirstTradeIdKey => "\"f\":"u8;
    private static ReadOnlySpan<byte> LastTradeIdKey => "\"l\":"u8;
    private static ReadOnlySpan<byte> TradeTimeKey => "\"T\":"u8;
    private static ReadOnlySpan<byte> IsBuyerMakerKey => "\"m\":"u8;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out AggTradeModel model, out long updateId)
    {
        model = default;
        updateId = 0;
        int cursor = data.StartsWith(EventPrefix) ? EventPrefix.Length : BinanceUtf8Reader.ObjectStart;

        // In the order the exchange writes them, so the normal case is one pass. The trailing M is never reached.
        if (!BinanceUtf8Reader.TryReadInt64(data, EventTimeKey, ref cursor, out model.EventTime)
            || !BinanceUtf8Reader.TryReadSymbol(data, SymbolKey, ref cursor, out model.Symbol)
            || !BinanceUtf8Reader.TryReadInt64(data, AggregateTradeIdKey, ref cursor, out model.AggregateTradeId)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, PriceKey, ref cursor, out model.Price)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, QuantityKey, ref cursor, out model.Quantity)
            || !BinanceUtf8Reader.TryReadInt64(data, FirstTradeIdKey, ref cursor, out model.FirstTradeId)
            || !BinanceUtf8Reader.TryReadInt64(data, LastTradeIdKey, ref cursor, out model.LastTradeId)
            || !BinanceUtf8Reader.TryReadInt64(data, TradeTimeKey, ref cursor, out model.TradeTime)
            || !BinanceUtf8Reader.TryReadBoolean(data, IsBuyerMakerKey, ref cursor, out model.IsBuyerMaker))
        {
            return false;
        }

        updateId = model.AggregateTradeId;
        return true;
    }
}
