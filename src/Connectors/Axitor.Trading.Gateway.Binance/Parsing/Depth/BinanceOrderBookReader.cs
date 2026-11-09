using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Depth;

/// <summary>Reads an order book snapshot of any market. Levels are read by the same reader as the depth streams, so a price reads to the same double on both.</summary>
internal static class BinanceOrderBookReader
{
    // The largest limit across markets, 5000 on spot.
    private const int MaxLevels = 5000;

    private static ReadOnlySpan<byte> SymbolKey => "\"symbol\":"u8;
    private static ReadOnlySpan<byte> PairKey => "\"pair\":"u8;
    private static ReadOnlySpan<byte> LastUpdateIdKey => "\"lastUpdateId\":"u8;
    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;
    private static ReadOnlySpan<byte> TransactionTimeKey => "\"T\":"u8;
    private static ReadOnlySpan<byte> BidsKey => "\"bids\":"u8;
    private static ReadOnlySpan<byte> AsksKey => "\"asks\":"u8;

    /// <summary>Cold path. Reads the snapshot into new level arrays. Futures send the event and transaction times, spot does not. Throws JsonException on an unexpected shape.</summary>
    public static OrderBookModel Read(ReadOnlySpan<byte> body, bool hasTimes)
    {
        OrderBookModel model = default;
        DepthLevel[] scratch = ArrayPool<DepthLevel>.Shared.Rent(MaxLevels);

        try
        {
            int cursor = ReadHeader(body, hasTimes, out model.LastUpdateId, out model.EventTime, out model.TransactionTime);

            ReadSide(body, BidsKey, ref cursor, scratch, out int bidCount);
            model.Bids = scratch.AsSpan(0, bidCount).ToArray();

            ReadSide(body, AsksKey, ref cursor, scratch, out int askCount);
            model.Asks = scratch.AsSpan(0, askCount).ToArray();
            return model;
        }
        finally
        {
            ArrayPool<DepthLevel>.Shared.Return(scratch);
        }
    }

    /// <summary>Reads the snapshot into the caller's buffers, best first, with nothing allocated. Throws JsonException on an unexpected shape or more levels than a buffer holds.</summary>
    public static OrderBookSnapshotModel ReadInto(ReadOnlySpan<byte> body, bool hasTimes, Span<DepthLevel> bids, Span<DepthLevel> asks)
    {
        OrderBookSnapshotModel model = default;
        int cursor = ReadHeader(body, hasTimes, out model.LastUpdateId, out model.EventTime, out model.TransactionTime);

        ReadSide(body, BidsKey, ref cursor, bids, out model.BidCount);
        ReadSide(body, AsksKey, ref cursor, asks, out model.AskCount);
        return model;
    }

    /// <summary>Reads everything before the levels and returns the cursor after it.</summary>
    private static int ReadHeader(ReadOnlySpan<byte> body, bool hasTimes, out long lastUpdateId, out long eventTime, out long transactionTime)
    {
        int cursor = BinanceUtf8Reader.ObjectStart;
        eventTime = 0;
        transactionTime = 0;

        if (body.IsEmpty || body[0] != (byte)'{')
        {
            ThrowMalformed();
        }

        // COIN-M opens with its symbol and pair.
        BinanceUtf8Reader.SkipOptional(body, SymbolKey, ref cursor);
        BinanceUtf8Reader.SkipOptional(body, PairKey, ref cursor);

        if (!BinanceUtf8Reader.TryReadInt64(body, LastUpdateIdKey, ref cursor, out lastUpdateId))
        {
            ThrowMalformed();
        }

        if (hasTimes
            && (!BinanceUtf8Reader.TryReadInt64(body, EventTimeKey, ref cursor, out eventTime)
                || !BinanceUtf8Reader.TryReadInt64(body, TransactionTimeKey, ref cursor, out transactionTime)))
        {
            ThrowMalformed();
        }

        return cursor;
    }

    private static void ReadSide(ReadOnlySpan<byte> body, ReadOnlySpan<byte> key, ref int cursor, Span<DepthLevel> levels, out int count)
    {
        if (!BinanceDepthReader.TryReadLevels(body, key, ref cursor, levels, out count))
        {
            ThrowMalformed();
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Order book response did not have the expected shape.");
}
