using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Book ticker parser of spot.</summary>
internal readonly struct SpotBookTickerParser : IBinanceStreamParser<SpotBookTickerModel>
{
    private static ReadOnlySpan<byte> UpdateIdKey => "\"u\":"u8;
    private static ReadOnlySpan<byte> SymbolKey => "\"s\":"u8;
    private static ReadOnlySpan<byte> BidPriceKey => "\"b\":"u8;
    private static ReadOnlySpan<byte> BidQuantityKey => "\"B\":"u8;
    private static ReadOnlySpan<byte> AskPriceKey => "\"a\":"u8;
    private static ReadOnlySpan<byte> AskQuantityKey => "\"A\":"u8;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out SpotBookTickerModel model, out long updateId)
    {
        model = default;
        updateId = 0;

        // Spot sends no event type: the update id comes first.
        int cursor = BinanceUtf8Reader.ObjectStart;

        // In the order the exchange writes them, so the normal case is one pass.
        if (!BinanceUtf8Reader.TryReadInt64(data, UpdateIdKey, ref cursor, out model.UpdateId)
            || !BinanceUtf8Reader.TryReadSymbol(data, SymbolKey, ref cursor, out model.Symbol)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, BidPriceKey, ref cursor, out model.BidPrice)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, BidQuantityKey, ref cursor, out model.BidQuantity)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, AskPriceKey, ref cursor, out model.AskPrice)
            || !BinanceUtf8Reader.TryReadQuotedDouble(data, AskQuantityKey, ref cursor, out model.AskQuantity))
        {
            return false;
        }

        updateId = model.UpdateId;
        return true;
    }
}
