using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Sbe;

namespace Axitor.Trading.Gateway.Binance.Parsing.Sbe;

/// <summary>DepthSnapshotStreamEvent parser.</summary>
internal readonly struct SbePartialDepthParser : IBinanceStreamParser<SbePartialDepthModel>
{
    // Root block of version 0.
    private const int RootLength = 18;
    private const int EventTimeOffset = 0;
    private const int UpdateIdOffset = 8;
    private const int PriceExponentOffset = 16;
    private const int QuantityExponentOffset = 17;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out SbePartialDepthModel model, out long updateId)
    {
        model = default;
        updateId = 0;

        if (!BinanceSbeReader.TryReadHeader(data, out _, out int blockLength) || blockLength < RootLength)
        {
            return false;
        }

        int root = BinanceSbeReader.HeaderLength;
        int cursor = root + blockLength;
        int priceExponent = BinanceSbeReader.ReadInt8(data, root + PriceExponentOffset);
        int quantityExponent = BinanceSbeReader.ReadInt8(data, root + QuantityExponentOffset);

        if (!BinanceSbeReader.TryReadLevels(data, ref cursor, priceExponent, quantityExponent, model.Bids, out model.BidCount)
            || !BinanceSbeReader.TryReadLevels(data, ref cursor, priceExponent, quantityExponent, model.Asks, out model.AskCount)
            || !BinanceSbeReader.TryReadSymbol(data, ref cursor, out model.Symbol))
        {
            return false;
        }

        model.EventTimeMicros = BinanceSbeReader.ReadInt64(data, root + EventTimeOffset);
        model.LastUpdateId = BinanceSbeReader.ReadInt64(data, root + UpdateIdOffset);

        updateId = model.LastUpdateId;
        return true;
    }
}
