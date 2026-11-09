using System.Globalization;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Parsing;

/// <summary>Turns a decimal sent as a mantissa and a power-of-ten exponent, e.g. in SBE, into the same double FastDecimalParser gives for its text.</summary>
/// <remarks>
/// DESIGN NOTE: When the mantissa and the power of ten are both exact doubles, one division or multiplication rounds the exact value once,
/// as a full parser does. The same number written with more or fewer trailing zeros is the same exact value, so it gives the same double.
/// So a price from a binary stream and the same price from a JSON snapshot land on one order book level.
/// Anything else is written as decimal text and parsed in full.
/// </remarks>
internal static class MantissaDecimal
{
    // Largest integer a double holds exactly.
    private const long MaxExactMantissa = 1L << 53;

    // Largest power of ten a double holds exactly.
    private const int MaxExactPowerOfTen = 22;

    // Sign, 19 digits, 'E', exponent sign and three digits.
    private const int MaxTextBytes = 32;

    private static ReadOnlySpan<double> PowersOfTen =>
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    /// <summary>The value of mantissa × 10^exponent.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble(long mantissa, int exponent)
    {
        // HOT: an exact mantissa and an exact power of ten, one rounding.
        if (-MaxExactMantissa <= mantissa && mantissa <= MaxExactMantissa)
        {
            if (-MaxExactPowerOfTen <= exponent && exponent < 0)
            {
                return mantissa / PowersOfTen[-exponent];
            }

            if (0 <= exponent && exponent <= MaxExactPowerOfTen)
            {
                return mantissa * PowersOfTen[exponent];
            }
        }

        return ToDoubleSlow(mantissa, exponent);
    }

    /// <summary>Cold path. Writes the number as text and parses it in full.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double ToDoubleSlow(long mantissa, int exponent)
    {
        Span<byte> text = stackalloc byte[MaxTextBytes];

        _ = mantissa.TryFormat(text, out int length, default, CultureInfo.InvariantCulture);
        text[length++] = (byte)'E';
        _ = exponent.TryFormat(text[length..], out int written, default, CultureInfo.InvariantCulture);
        length += written;

        _ = double.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
        return value;
    }
}
