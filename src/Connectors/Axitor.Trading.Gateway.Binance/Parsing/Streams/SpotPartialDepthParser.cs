using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Streams;

/// <summary>Partial depth parser of spot.</summary>
internal readonly struct SpotPartialDepthParser : IBinanceStreamParser<SpotPartialDepthModel>
{
    private static ReadOnlySpan<byte> LastUpdateIdKey => "\"lastUpdateId\":"u8;
    private static ReadOnlySpan<byte> BidsKey => "\"bids\":"u8;
    private static ReadOnlySpan<byte> AsksKey => "\"asks\":"u8;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out SpotPartialDepthModel model, out long updateId)
    {
        model = default;
        updateId = 0;

        // Spot sends no event type or symbol: the update id comes first.
        int cursor = BinanceUtf8Reader.ObjectStart;

        if (!BinanceUtf8Reader.TryReadInt64(data, LastUpdateIdKey, ref cursor, out model.LastUpdateId)
            || !BinanceDepthReader.TryReadLevels(data, BidsKey, ref cursor, model.Bids, out model.BidCount)
            || !BinanceDepthReader.TryReadLevels(data, AsksKey, ref cursor, model.Asks, out model.AskCount))
        {
            return false;
        }

        updateId = model.LastUpdateId;
        return true;
    }
}
