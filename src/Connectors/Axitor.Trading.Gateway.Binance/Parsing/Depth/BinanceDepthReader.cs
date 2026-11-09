using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Binance.Parsing.Depth;

/// <summary>Reads order book level arrays, [["price","quantity"],...], straight from UTF-8.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Depth streams and the depth snapshot read levels here, so one price text always becomes one double.
/// The local order book keys its levels by price, and a snapshot and a stream reading the same price differently would split one level in two.
/// </para>
/// <para>
/// DESIGN NOTE: The exchange writes levels compactly, with no spaces. Levels are read in one pass on exactly that layout;
/// any other layout reads as malformed rather than being guessed at.
/// </para>
/// </remarks>
internal static class BinanceDepthReader
{
    // Starting size of a growable buffer; it doubles when a message has more levels.
    private const int InitialLevels = 16;

    private static ReadOnlySpan<byte> LevelStart => "[\""u8;
    private static ReadOnlySpan<byte> LevelSeparator => "\",\""u8;
    private static ReadOnlySpan<byte> LevelEnd => "\"]"u8;

    /// <summary>Reads the level array of a key into the buffer, growing it when full, and moves the cursor past the array.</summary>
    public static bool TryReadLevels(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, ref DepthLevel[] levels, out int count)
    {
        count = 0;
        int position = cursor;

        if (!TryOpenArray(data, key, ref position, out bool isEmpty))
        {
            return false;
        }

        while (!isEmpty)
        {
            if (count == levels.Length)
            {
                levels = Grow(levels);
            }

            if (!TryReadLevel(data, ref position, out levels[count]))
            {
                count = 0;
                return false;
            }

            count++;

            if (!TryReadSeparator(data, ref position, out isEmpty))
            {
                count = 0;
                return false;
            }
        }

        cursor = position;
        return true;
    }

    /// <summary>Reads the level array of a key into a fixed span and moves the cursor past the array. False when more levels come than fit.</summary>
    public static bool TryReadLevels(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, Span<DepthLevel> levels, out int count)
    {
        count = 0;
        int position = cursor;

        if (!TryOpenArray(data, key, ref position, out bool isEmpty))
        {
            return false;
        }

        while (!isEmpty)
        {
            if (count == levels.Length || !TryReadLevel(data, ref position, out levels[count]))
            {
                count = 0;
                return false;
            }

            count++;

            if (!TryReadSeparator(data, ref position, out isEmpty))
            {
                count = 0;
                return false;
            }
        }

        cursor = position;
        return true;
    }

    /// <summary>Moves past the opening bracket of the key's array; an empty array is consumed whole.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryOpenArray(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int position, out bool isEmpty)
    {
        isEmpty = false;

        if (!BinanceUtf8Reader.TrySeekValue(data, key, ref position)
            || (uint)data.Length <= (uint)position
            || data[position] != (byte)'[')
        {
            return false;
        }

        position++;

        if ((uint)position < (uint)data.Length && data[position] == (byte)']')
        {
            position++;
            isEmpty = true;
        }

        return true;
    }

    /// <summary>Reads one ["price","quantity"] and moves past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadLevel(ReadOnlySpan<byte> data, ref int position, out DepthLevel level)
    {
        level = default;
        int index = position;

        if (!data[index..].StartsWith(LevelStart))
        {
            return false;
        }

        index += LevelStart.Length;

        if (!FastDecimalParser.TryParse(data[index..], out level.Price, out int consumed))
        {
            return false;
        }

        index += consumed;

        if (!data[index..].StartsWith(LevelSeparator))
        {
            return false;
        }

        index += LevelSeparator.Length;

        if (!FastDecimalParser.TryParse(data[index..], out level.Quantity, out consumed))
        {
            return false;
        }

        index += consumed;

        if (!data[index..].StartsWith(LevelEnd))
        {
            return false;
        }

        position = index + LevelEnd.Length;
        return true;
    }

    /// <summary>Reads the comma before the next level or the closing bracket of the array.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadSeparator(ReadOnlySpan<byte> data, ref int position, out bool isEnd)
    {
        isEnd = false;

        if ((uint)data.Length <= (uint)position)
        {
            return false;
        }

        byte next = data[position++];

        if (next == (byte)']')
        {
            isEnd = true;
            return true;
        }

        return next == (byte)',';
    }

    /// <summary>Cold path. A larger buffer for a message with more levels than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DepthLevel[] Grow(DepthLevel[] levels)
    {
        DepthLevel[] larger = new DepthLevel[Math.Max(InitialLevels, levels.Length * 2)];
        levels.CopyTo(larger, 0);
        return larger;
    }
}
