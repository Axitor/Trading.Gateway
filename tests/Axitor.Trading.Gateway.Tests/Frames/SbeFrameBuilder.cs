using System.Buffers.Binary;

namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>Builds spot SBE stream messages byte by byte, little-endian, laid out as schema 1 version 0.</summary>
internal sealed class SbeFrameBuilder
{
    public const int SchemaId = 1;
    public const int TradesTemplateId = 10000;
    public const int BestBidAskTemplateId = 10001;
    public const int DepthSnapshotTemplateId = 10002;
    public const int DepthDiffTemplateId = 10003;

    // Root blocks and group entries of version 0.
    public const int TradesRootLength = 18;
    public const int TradeEntryLength = 25;
    public const int BestBidAskRootLength = 50;
    public const int DepthSnapshotRootLength = 18;
    public const int DepthDiffRootLength = 26;
    public const int LevelEntryLength = 16;

    private readonly List<byte> _bytes = [];

    public SbeFrameBuilder Header(int blockLength, int templateId, int schemaId = SchemaId, int version = 0) =>
        UInt16(blockLength).UInt16(templateId).UInt16(schemaId).UInt16(version);

    public SbeFrameBuilder UInt16(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return Add(bytes);
    }

    public SbeFrameBuilder UInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return Add(bytes);
    }

    public SbeFrameBuilder Int64(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return Add(bytes);
    }

    public SbeFrameBuilder Int8(int value)
    {
        _bytes.Add((byte)(sbyte)value);
        return this;
    }

    public SbeFrameBuilder Byte(byte value)
    {
        _bytes.Add(value);
        return this;
    }

    /// <summary>Bytes a newer schema version appends to a block or an entry.</summary>
    public SbeFrameBuilder Zeros(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _bytes.Add(0);
        }

        return this;
    }

    /// <summary>groupSizeEncoding: entry length and a 32-bit count.</summary>
    public SbeFrameBuilder Group(int blockLength, uint count) => UInt16(blockLength).UInt32(count);

    /// <summary>groupSize16Encoding: entry length and a 16-bit count.</summary>
    public SbeFrameBuilder Group16(int blockLength, int count) => UInt16(blockLength).UInt16(count);

    public SbeFrameBuilder VarString8(string text)
    {
        _bytes.Add((byte)text.Length);

        foreach (char c in text)
        {
            _bytes.Add((byte)c);
        }

        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    public static byte[] Trades(
        string symbol,
        long eventTimeMicros,
        long transactTimeMicros,
        int priceExponent,
        int quantityExponent,
        params ReadOnlySpan<(long Id, long Price, long Quantity, bool IsBuyerMaker)> trades)
    {
        SbeFrameBuilder builder = new SbeFrameBuilder()
            .Header(TradesRootLength, TradesTemplateId)
            .Int64(eventTimeMicros)
            .Int64(transactTimeMicros)
            .Int8(priceExponent)
            .Int8(quantityExponent)
            .Group(TradeEntryLength, (uint)trades.Length);

        foreach ((long id, long price, long quantity, bool isBuyerMaker) in trades)
        {
            builder.Int64(id).Int64(price).Int64(quantity).Byte(isBuyerMaker ? (byte)1 : (byte)0);
        }

        return builder.VarString8(symbol).ToArray();
    }

    public static byte[] BestBidAsk(
        string symbol,
        long eventTimeMicros,
        long updateId,
        int priceExponent,
        int quantityExponent,
        long bidPrice,
        long bidQuantity,
        long askPrice,
        long askQuantity,
        int extraRootBytes = 0,
        int schemaId = SchemaId) =>
        new SbeFrameBuilder()
            .Header(BestBidAskRootLength + extraRootBytes, BestBidAskTemplateId, schemaId)
            .Int64(eventTimeMicros)
            .Int64(updateId)
            .Int8(priceExponent)
            .Int8(quantityExponent)
            .Int64(bidPrice)
            .Int64(bidQuantity)
            .Int64(askPrice)
            .Int64(askQuantity)
            .Zeros(extraRootBytes)
            .VarString8(symbol)
            .ToArray();

    public static byte[] DepthSnapshot(
        string symbol,
        long eventTimeMicros,
        long updateId,
        int priceExponent,
        int quantityExponent,
        (long Price, long Quantity)[] bids,
        (long Price, long Quantity)[] asks)
    {
        SbeFrameBuilder builder = new SbeFrameBuilder()
            .Header(DepthSnapshotRootLength, DepthSnapshotTemplateId)
            .Int64(eventTimeMicros)
            .Int64(updateId)
            .Int8(priceExponent)
            .Int8(quantityExponent);

        return builder.Levels(bids, 0).Levels(asks, 0).VarString8(symbol).ToArray();
    }

    public static byte[] DepthDiff(
        string symbol,
        long eventTimeMicros,
        long firstUpdateId,
        long lastUpdateId,
        int priceExponent,
        int quantityExponent,
        (long Price, long Quantity)[] bids,
        (long Price, long Quantity)[] asks,
        int extraEntryBytes = 0)
    {
        SbeFrameBuilder builder = new SbeFrameBuilder()
            .Header(DepthDiffRootLength, DepthDiffTemplateId)
            .Int64(eventTimeMicros)
            .Int64(firstUpdateId)
            .Int64(lastUpdateId)
            .Int8(priceExponent)
            .Int8(quantityExponent);

        return builder.Levels(bids, extraEntryBytes).Levels(asks, extraEntryBytes).VarString8(symbol).ToArray();
    }

    private SbeFrameBuilder Levels((long Price, long Quantity)[] levels, int extraEntryBytes)
    {
        Group16(LevelEntryLength + extraEntryBytes, levels.Length);

        foreach ((long price, long quantity) in levels)
        {
            Int64(price).Int64(quantity).Zeros(extraEntryBytes);
        }

        return this;
    }

    private SbeFrameBuilder Add(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            _bytes.Add(value);
        }

        return this;
    }
}
