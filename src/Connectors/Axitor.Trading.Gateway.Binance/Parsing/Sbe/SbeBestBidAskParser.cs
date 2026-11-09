using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Sbe;

namespace Axitor.Trading.Gateway.Binance.Parsing.Sbe;

/// <summary>BestBidAskStreamEvent parser.</summary>
internal readonly struct SbeBestBidAskParser : IBinanceStreamParser<SbeBestBidAskModel>
{
    // Root block of version 0.
    private const int RootLength = 50;
    private const int EventTimeOffset = 0;
    private const int UpdateIdOffset = 8;
    private const int PriceExponentOffset = 16;
    private const int QuantityExponentOffset = 17;
    private const int BidPriceOffset = 18;
    private const int BidQuantityOffset = 26;
    private const int AskPriceOffset = 34;
    private const int AskQuantityOffset = 42;

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> data, out SbeBestBidAskModel model, out long updateId)
    {
        model = default;
        updateId = 0;

        if (!BinanceSbeReader.TryReadHeader(data, out _, out int blockLength) || blockLength < RootLength)
        {
            return false;
        }

        int root = BinanceSbeReader.HeaderLength;
        int cursor = root + blockLength;

        if (!BinanceSbeReader.TryReadSymbol(data, ref cursor, out model.Symbol))
        {
            return false;
        }

        int priceExponent = BinanceSbeReader.ReadInt8(data, root + PriceExponentOffset);
        int quantityExponent = BinanceSbeReader.ReadInt8(data, root + QuantityExponentOffset);

        model.EventTimeMicros = BinanceSbeReader.ReadInt64(data, root + EventTimeOffset);
        model.UpdateId = BinanceSbeReader.ReadInt64(data, root + UpdateIdOffset);
        model.BidPrice = BinanceSbeReader.ReadDecimal(data, root + BidPriceOffset, priceExponent);
        model.BidQuantity = BinanceSbeReader.ReadDecimal(data, root + BidQuantityOffset, quantityExponent);
        model.AskPrice = BinanceSbeReader.ReadDecimal(data, root + AskPriceOffset, priceExponent);
        model.AskQuantity = BinanceSbeReader.ReadDecimal(data, root + AskQuantityOffset, quantityExponent);

        updateId = model.UpdateId;
        return true;
    }
}
