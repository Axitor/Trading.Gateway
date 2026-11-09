using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Binance.Parsing.Sbe;

/// <summary>Reads the parts of the spot stream SBE schema straight from the message: header, groups, strings and decimals.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Every read is checked against the message length first, so a broken message reads as false and never throws on the receive loop.
/// </para>
/// <para>
/// DESIGN NOTE: Root blocks and group entries are stepped over by the lengths the message sends, not the ones of version 0,
/// so fields a newer schema version appends to them are skipped.
/// </para>
/// </remarks>
internal static class BinanceSbeReader
{
    /// <summary>Schema id of the spot streams.</summary>
    public const int SchemaId = 1;

    /// <summary>Template id of TradesStreamEvent.</summary>
    public const int TradesTemplateId = 10000;

    /// <summary>Template id of BestBidAskStreamEvent.</summary>
    public const int BestBidAskTemplateId = 10001;

    /// <summary>Template id of DepthSnapshotStreamEvent.</summary>
    public const int DepthSnapshotTemplateId = 10002;

    /// <summary>Template id of DepthDiffStreamEvent.</summary>
    public const int DepthDiffTemplateId = 10003;

    /// <summary>Length of the message header; the root block follows it.</summary>
    public const int HeaderLength = 8;

    // Message header: blockLength, templateId, schemaId, version.
    private const int HeaderBlockLengthOffset = 0;
    private const int HeaderTemplateIdOffset = 2;
    private const int HeaderSchemaIdOffset = 4;

    // groupSizeEncoding: blockLength uint16, numInGroup uint32.
    private const int GroupHeaderLength = 6;

    // groupSize16Encoding: blockLength uint16, numInGroup uint16.
    private const int Group16HeaderLength = 4;

    // Price level entry of version 0: price, then quantity.
    private const int LevelEntryLength = 16;
    private const int LevelPriceOffset = 0;
    private const int LevelQuantityOffset = 8;

    /// <summary>Reads the message header. False when the message is shorter than its root block or of another schema.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadHeader(ReadOnlySpan<byte> message, out int templateId, out int blockLength)
    {
        templateId = 0;
        blockLength = 0;

        if (message.Length < HeaderLength)
        {
            return false;
        }

        int length = ReadUInt16(message, HeaderBlockLengthOffset);

        if (ReadUInt16(message, HeaderSchemaIdOffset) != SchemaId || message.Length - HeaderLength < length)
        {
            return false;
        }

        templateId = ReadUInt16(message, HeaderTemplateIdOffset);
        blockLength = length;
        return true;
    }

    /// <summary>Finds the symbol at the end of a stream message by stepping over its groups. False for an unknown template or a broken message.</summary>
    public static bool TryFindSymbol(ReadOnlySpan<byte> message, out int templateId, out int symbolStart, out int symbolLength)
    {
        symbolStart = 0;
        symbolLength = 0;

        if (!TryReadHeader(message, out templateId, out int blockLength))
        {
            return false;
        }

        int cursor = HeaderLength + blockLength;

        switch (templateId)
        {
            case TradesTemplateId:
                if (!TrySkipGroup(message, ref cursor))
                {
                    return false;
                }
                break;

            case BestBidAskTemplateId:
                break;

            case DepthSnapshotTemplateId:
            case DepthDiffTemplateId:
                if (!TrySkipGroup16(message, ref cursor) || !TrySkipGroup16(message, ref cursor))
                {
                    return false;
                }
                break;

            default:
                return false;
        }

        return TryReadVarString8(message, ref cursor, out symbolStart, out symbolLength) && 0 < symbolLength;
    }

    /// <summary>Reads a group header with a 32-bit count and moves the cursor to its first entry. Every entry is known to fit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadGroup(ReadOnlySpan<byte> message, ref int cursor, int minEntryLength, out int entryLength, out int count)
    {
        entryLength = 0;
        count = 0;

        if (message.Length - GroupHeaderLength < cursor)
        {
            return false;
        }

        int length = ReadUInt16(message, cursor);
        uint number = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(cursor + 2, sizeof(uint)));
        return TryFitGroup(message.Length, ref cursor, GroupHeaderLength, length, number, minEntryLength, out entryLength, out count);
    }

    /// <summary>Reads a group header with a 16-bit count and moves the cursor to its first entry. Every entry is known to fit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadGroup16(ReadOnlySpan<byte> message, ref int cursor, int minEntryLength, out int entryLength, out int count)
    {
        entryLength = 0;
        count = 0;

        if (message.Length - Group16HeaderLength < cursor)
        {
            return false;
        }

        int length = ReadUInt16(message, cursor);
        uint number = ReadUInt16(message, cursor + 2);
        return TryFitGroup(message.Length, ref cursor, Group16HeaderLength, length, number, minEntryLength, out entryLength, out count);
    }

    /// <summary>Reads a group of price levels into a buffer, replaced by a larger one when they do not fit, and moves the cursor past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadLevels(ReadOnlySpan<byte> message, ref int cursor, int priceExponent, int quantityExponent, ref DepthLevel[] levels, out int count)
    {
        if (!TryReadGroup16(message, ref cursor, LevelEntryLength, out int entryLength, out count))
        {
            return false;
        }

        if (levels.Length < count)
        {
            levels = Grow(levels, count);
        }

        ReadLevelEntries(message, cursor, entryLength, priceExponent, quantityExponent, levels.AsSpan(0, count));
        cursor += entryLength * count;
        return true;
    }

    /// <summary>Reads a group of price levels into a fixed span and moves the cursor past it. False when more levels come than fit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadLevels(ReadOnlySpan<byte> message, ref int cursor, int priceExponent, int quantityExponent, Span<DepthLevel> levels, out int count)
    {
        if (!TryReadGroup16(message, ref cursor, LevelEntryLength, out int entryLength, out count) || levels.Length < count)
        {
            count = 0;
            return false;
        }

        ReadLevelEntries(message, cursor, entryLength, priceExponent, quantityExponent, levels[..count]);
        cursor += entryLength * count;
        return true;
    }

    /// <summary>Reads the varString8 symbol at the cursor and moves past it. False when it does not fit or is not a valid symbol.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadSymbol(ReadOnlySpan<byte> message, ref int cursor, out Symbol symbol)
    {
        if (!TryReadVarString8(message, ref cursor, out int start, out int length))
        {
            symbol = default;
            return false;
        }

        return Symbol.TryParse(message.Slice(start, length), out symbol);
    }

    /// <summary>Reads a signed 64-bit integer. The offset must be checked against the message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64(ReadOnlySpan<byte> message, int offset) => BinaryPrimitives.ReadInt64LittleEndian(message.Slice(offset, sizeof(long)));

    /// <summary>Reads a signed 8-bit integer, e.g. an exponent. The offset must be checked against the message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt8(ReadOnlySpan<byte> message, int offset) => (sbyte)message[offset];

    /// <summary>Reads a mantissa and applies its exponent. The offset must be checked against the message.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ReadDecimal(ReadOnlySpan<byte> message, int offset, int exponent) => MantissaDecimal.ToDouble(ReadInt64(message, offset), exponent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16(ReadOnlySpan<byte> message, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(message.Slice(offset, sizeof(ushort)));

    /// <summary>Checks that every entry fits the message and moves the cursor to the first one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryFitGroup(int messageLength, ref int cursor, int headerLength, int length, uint number, int minEntryLength, out int entryLength, out int count)
    {
        entryLength = 0;
        count = 0;

        int entriesStart = cursor + headerLength;

        if (length < minEntryLength || int.MaxValue < number || messageLength - entriesStart < (long)length * number)
        {
            return false;
        }

        cursor = entriesStart;
        entryLength = length;
        count = (int)number;
        return true;
    }

    private static bool TrySkipGroup(ReadOnlySpan<byte> message, ref int cursor)
    {
        if (!TryReadGroup(message, ref cursor, 0, out int entryLength, out int count))
        {
            return false;
        }

        cursor += entryLength * count;
        return true;
    }

    private static bool TrySkipGroup16(ReadOnlySpan<byte> message, ref int cursor)
    {
        if (!TryReadGroup16(message, ref cursor, 0, out int entryLength, out int count))
        {
            return false;
        }

        cursor += entryLength * count;
        return true;
    }

    /// <summary>Reads a varString8 at the cursor and moves past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadVarString8(ReadOnlySpan<byte> message, ref int cursor, out int start, out int length)
    {
        start = 0;
        length = 0;

        if ((uint)message.Length <= (uint)cursor)
        {
            return false;
        }

        int textLength = message[cursor];
        int textStart = cursor + 1;

        if (message.Length - textStart < textLength)
        {
            return false;
        }

        start = textStart;
        length = textLength;
        cursor = textStart + textLength;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadLevelEntries(ReadOnlySpan<byte> message, int start, int entryLength, int priceExponent, int quantityExponent, Span<DepthLevel> levels)
    {
        int offset = start;

        for (int i = 0; i < levels.Length; i++)
        {
            levels[i].Price = ReadDecimal(message, offset + LevelPriceOffset, priceExponent);
            levels[i].Quantity = ReadDecimal(message, offset + LevelQuantityOffset, quantityExponent);
            offset += entryLength;
        }
    }

    /// <summary>Cold path. A larger buffer for a message with more levels than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DepthLevel[] Grow(DepthLevel[] levels, int required) => new DepthLevel[Math.Max(required, levels.Length * 2)];
}
