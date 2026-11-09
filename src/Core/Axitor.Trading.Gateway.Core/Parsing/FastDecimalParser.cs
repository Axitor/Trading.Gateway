using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Parsing;

/// <summary>Parses a plain decimal number from UTF-8, e.g. a price or quantity, to the same double as a full parser.</summary>
/// <remarks>
/// DESIGN NOTE: Exchanges send prices and quantities as short plain decimals. When the digits fit a double exactly and there are
/// at most 22 decimal places, the value is the digits divided by a power of ten. Both are exact doubles, so the division rounds
/// the same way a full parser does. Anything else, e.g. an exponent or too many digits, goes to Utf8Parser.
/// </remarks>
internal static class FastDecimalParser
{
    // Largest integer a double holds exactly.
    private const ulong MaxExactMantissa = 1UL << 53;

    // Largest power of ten a double holds exactly.
    private const int MaxExactPowerOfTen = 22;

    // Any 19 digits fit a ulong.
    private const int MaxDigits = 19;

    private static ReadOnlySpan<double> PowersOfTen =>
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    /// <summary>Parses the number at the start of the text and says how many bytes it took. False when no number starts there.</summary>
    public static bool TryParse(ReadOnlySpan<byte> text, out double value, out int bytesConsumed)
    {
        int index = 0;
        bool isNegative = false;

        if (!text.IsEmpty && text[0] == (byte)'-')
        {
            isNegative = true;
            index = 1;
        }

        ulong mantissa = 0;
        int significantDigits = 0;

        int integerStart = index;
        ReadDigits(text, ref index, ref mantissa, ref significantDigits);
        int integerDigits = index - integerStart;
        int fractionDigits = 0;

        if ((uint)index < (uint)text.Length && text[index] == (byte)'.')
        {
            index++;
            int fractionStart = index;
            ReadDigits(text, ref index, ref mantissa, ref significantDigits);
            fractionDigits = index - fractionStart;
        }

        if (integerDigits + fractionDigits == 0
            || MaxDigits < significantDigits
            || MaxExactMantissa < mantissa
            || MaxExactPowerOfTen < fractionDigits
            || ((uint)index < (uint)text.Length && (text[index] | 0x20) == (byte)'e'))
        {
            return TryParseSlow(text, out value, out bytesConsumed);
        }

        double result = mantissa / PowersOfTen[fractionDigits];

        value = isNegative ? -result : result;
        bytesConsumed = index;
        return true;
    }

    /// <summary>Adds digits to the mantissa until a non-digit. Leading zeros are not counted as significant.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadDigits(ReadOnlySpan<byte> text, ref int index, ref ulong mantissa, ref int significantDigits)
    {
        while ((uint)index < (uint)text.Length)
        {
            uint digit = (uint)(text[index] - '0');

            if (9 < digit)
            {
                return;
            }

            // Wraps past 19 digits; the count sends such a number to the slow path.
            mantissa = mantissa * 10 + digit;
            significantDigits += mantissa == 0 ? 0 : 1;
            index++;
        }
    }

    /// <summary>Cold path. Full parser for what the fast path does not take.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryParseSlow(ReadOnlySpan<byte> text, out double value, out int bytesConsumed) =>
        Utf8Parser.TryParse(text, out value, out bytesConsumed);
}
