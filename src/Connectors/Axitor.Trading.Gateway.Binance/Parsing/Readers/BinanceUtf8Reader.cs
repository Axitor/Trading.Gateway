using System.Buffers.Text;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Binance.Parsing.Readers;

/// <summary>Reads fields of a flat JSON object straight from UTF-8, with no JSON reader. Keys are passed with quotes and colon, e.g. "\"u\":".</summary>
/// <remarks>
/// DESIGN NOTE: The exchange writes keys in a stable order, so each read first checks right at the cursor.
/// A skipped field is found by a search ahead, a changed order by a search of the whole object.
/// Fields the parser does not need are skipped right at the cursor, so a field the exchange adds keeps the next read on the fast path.
/// Values are numbers, booleans, short strings and arrays of them, with no colons inside, so a key cannot match inside a value.
/// </remarks>
internal static class BinanceUtf8Reader
{
    /// <summary>Cursor for the first key: right after the opening brace.</summary>
    public const int ObjectStart = 1;

    /// <summary>Reads an unquoted integer and moves the cursor past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadInt64(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, out long value)
    {
        int start = FindValue(data, key, cursor);

        if (start < 0 || !Utf8Parser.TryParse(data[start..], out value, out int consumed))
        {
            value = 0;
            return false;
        }

        cursor = start + consumed;
        return true;
    }

    /// <summary>Reads a quoted number, as prices and quantities come, and moves the cursor past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadQuotedDouble(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, out double value)
    {
        int start = FindValue(data, key, cursor);

        if (start < 0 || (uint)data.Length <= (uint)start || data[start] != (byte)'"'
            || !FastDecimalParser.TryParse(data[(start + 1)..], out value, out int consumed))
        {
            value = 0;
            return false;
        }

        int end = start + 1 + consumed;

        if ((uint)data.Length <= (uint)end || data[end] != (byte)'"')
        {
            value = 0;
            return false;
        }

        cursor = end + 1;
        return true;
    }

    /// <summary>Reads a quoted symbol and moves the cursor past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadSymbol(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, out Symbol value)
    {
        int start = FindValue(data, key, cursor);

        if (start < 0 || (uint)data.Length <= (uint)start || data[start] != (byte)'"')
        {
            value = default;
            return false;
        }

        int length = data[(start + 1)..].IndexOf((byte)'"');

        if (length < 0 || !Symbol.TryParse(data.Slice(start + 1, length), out value))
        {
            value = default;
            return false;
        }

        cursor = start + length + 2;
        return true;
    }

    /// <summary>Reads true or false and moves the cursor past it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadBoolean(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor, out bool value)
    {
        int start = FindValue(data, key, cursor);

        if (0 <= start)
        {
            ReadOnlySpan<byte> rest = data[start..];

            if (rest.StartsWith("true"u8))
            {
                value = true;
                cursor = start + 4;
                return true;
            }

            if (rest.StartsWith("false"u8))
            {
                value = false;
                cursor = start + 5;
                return true;
            }
        }

        value = false;
        return false;
    }

    /// <summary>Moves the cursor to the value of a key, e.g. the opening bracket of an array.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySeekValue(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor)
    {
        int start = FindValue(data, key, cursor);

        if (start < 0)
        {
            return false;
        }

        cursor = start;
        return true;
    }

    /// <summary>Moves the cursor past a scalar field the parser does not need, if it is right at the cursor. Otherwise the cursor stays.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SkipOptional(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ref int cursor)
    {
        int position = cursor;

        if ((uint)position < (uint)data.Length && data[position] == (byte)',')
        {
            position++;
        }

        if (!data[position..].StartsWith(key))
        {
            return;
        }

        position += key.Length;

        if ((uint)data.Length <= (uint)position)
        {
            return;
        }

        if (data[position] == (byte)'"')
        {
            int length = data[(position + 1)..].IndexOf((byte)'"');

            if (0 <= length)
            {
                cursor = position + length + 2;
            }

            return;
        }

        // A number or a boolean ends at the next comma or the closing brace.
        int end = data[position..].IndexOfAny((byte)',', (byte)'}');

        if (0 <= end)
        {
            cursor = position + end;
        }
    }

    /// <summary>Where the value of a key starts, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindValue(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, int cursor)
    {
        int position = cursor;

        // HOT: the key follows the previous value right after its comma.
        if ((uint)position < (uint)data.Length && data[position] == (byte)',')
        {
            position++;
        }

        if (data[position..].StartsWith(key))
        {
            return position + key.Length;
        }

        return FindValueSlow(data, key, cursor);
    }

    /// <summary>Cold path. A skipped field lies between: search ahead first, then the whole object for a changed key order.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FindValueSlow(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, int cursor)
    {
        int ahead = data[cursor..].IndexOf(key);

        if (0 <= ahead)
        {
            return cursor + ahead + key.Length;
        }

        int index = data.IndexOf(key);
        return index < 0 ? -1 : index + key.Length;
    }
}
