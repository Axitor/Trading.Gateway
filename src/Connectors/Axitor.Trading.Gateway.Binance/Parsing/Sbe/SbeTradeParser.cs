using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Sbe;

namespace Axitor.Trading.Gateway.Binance.Parsing.Sbe;

/// <summary>TradesStreamEvent parser. One event can carry several trades: open it once, then read each trade.</summary>
internal static class SbeTradeParser
{
    // Root block of version 0.
    private const int RootLength = 18;
    private const int EventTimeOffset = 0;
    private const int TransactionTimeOffset = 8;
    private const int PriceExponentOffset = 16;
    private const int QuantityExponentOffset = 17;

    // Trade entry of version 0. isBestMatch is a constant and takes no bytes.
    private const int EntryLength = 25;
    private const int IdOffset = 0;
    private const int PriceOffset = 8;
    private const int QuantityOffset = 16;
    private const int IsBuyerMakerOffset = 24;

    /// <summary>Reads the parts every trade shares into the model and finds the trades. False when the message is broken.</summary>
    public static bool TryOpen(
        ReadOnlySpan<byte> data,
        out SbeTradeModel model,
        out int entriesStart,
        out int entryLength,
        out int count,
        out int priceExponent,
        out int quantityExponent)
    {
        model = default;
        entriesStart = 0;
        priceExponent = 0;
        quantityExponent = 0;

        if (!BinanceSbeReader.TryReadHeader(data, out _, out int blockLength) || blockLength < RootLength)
        {
            entryLength = 0;
            count = 0;
            return false;
        }

        int root = BinanceSbeReader.HeaderLength;
        int cursor = root + blockLength;

        if (!BinanceSbeReader.TryReadGroup(data, ref cursor, EntryLength, out entryLength, out count))
        {
            return false;
        }

        entriesStart = cursor;
        cursor += entryLength * count;

        if (!BinanceSbeReader.TryReadSymbol(data, ref cursor, out model.Symbol))
        {
            return false;
        }

        model.EventTimeMicros = BinanceSbeReader.ReadInt64(data, root + EventTimeOffset);
        model.TransactionTimeMicros = BinanceSbeReader.ReadInt64(data, root + TransactionTimeOffset);
        priceExponent = BinanceSbeReader.ReadInt8(data, root + PriceExponentOffset);
        quantityExponent = BinanceSbeReader.ReadInt8(data, root + QuantityExponentOffset);
        return true;
    }

    /// <summary>Reads one trade into the model. The entry must come from TryOpen.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadTrade(ReadOnlySpan<byte> data, int entryStart, int priceExponent, int quantityExponent, ref SbeTradeModel model)
    {
        model.TradeId = BinanceSbeReader.ReadInt64(data, entryStart + IdOffset);
        model.Price = BinanceSbeReader.ReadDecimal(data, entryStart + PriceOffset, priceExponent);
        model.Quantity = BinanceSbeReader.ReadDecimal(data, entryStart + QuantityOffset, quantityExponent);
        model.IsBuyerMaker = data[entryStart + IsBuyerMakerOffset] == 1;
    }
}
