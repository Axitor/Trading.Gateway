using System.Globalization;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Transport.Common.Payload;

/// <summary>Writes a double as plain decimal UTF-8 text, never in exponent form, rounded to 15 significant digits.</summary>
/// <remarks>
/// DESIGN NOTE: Exchanges reject exponent form such as 1E-07. Fifteen significant digits is the most a double keeps exactly,
/// so rounding to them drops binary noise like 0.30000000000000004 and keeps every price and quantity an exchange lists.
/// </remarks>
internal static class PlainDecimalFormatter
{
    // Longest short form: sign, 15 digits, point, E, exponent sign, three exponent digits.
    private const int MaxShortFormBytes = 24;
    private const int MaxSignificantDigits = 15;

    /// <summary>Writes the value. False when the destination is too short.</summary>
    public static bool TryFormat(double value, Span<byte> destination, out int bytesWritten)
    {
        Span<byte> text = stackalloc byte[MaxShortFormBytes];

        if (!value.TryFormat(text, out int length, "G15", CultureInfo.InvariantCulture))
        {
            bytesWritten = 0;
            return false;
        }

        ReadOnlySpan<byte> shortForm = text[..length];
        int exponentAt = shortForm.IndexOf((byte)'E');

        if (exponentAt < 0)
        {
            if (destination.Length < shortForm.Length)
            {
                bytesWritten = 0;
                return false;
            }

            shortForm.CopyTo(destination);
            bytesWritten = shortForm.Length;
            return true;
        }

        return TryExpand(shortForm, exponentAt, destination, out bytesWritten);
    }

    /// <summary>Cold path. Rewrites d.dddE±x as plain digits with the point moved.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryExpand(ReadOnlySpan<byte> shortForm, int exponentAt, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        ReadOnlySpan<byte> mantissa = shortForm[..exponentAt];
        int exponent = ReadExponent(shortForm[(exponentAt + 1)..]);
        bool isNegative = mantissa[0] == (byte)'-';

        if (isNegative)
        {
            mantissa = mantissa[1..];
        }

        Span<byte> digits = stackalloc byte[MaxSignificantDigits];
        int digitCount = 0;

        for (int i = 0; i < mantissa.Length; i++)
        {
            if (mantissa[i] != (byte)'.')
            {
                digits[digitCount++] = mantissa[i];
            }
        }

        // The mantissa has one digit before its point, so this is where the point lands.
        int pointAt = 1 + exponent;
        int signLength = isNegative ? 1 : 0;

        int bodyLength = pointAt <= 0
            ? 2 - pointAt + digitCount
            : digitCount <= pointAt ? pointAt : digitCount + 1;

        if (destination.Length < signLength + bodyLength)
        {
            return false;
        }

        int position = 0;

        if (isNegative)
        {
            destination[position++] = (byte)'-';
        }

        if (pointAt <= 0)
        {
            destination[position++] = (byte)'0';
            destination[position++] = (byte)'.';
            destination.Slice(position, -pointAt).Fill((byte)'0');
            position += -pointAt;
            digits[..digitCount].CopyTo(destination[position..]);
            position += digitCount;
        }
        else if (digitCount <= pointAt)
        {
            digits[..digitCount].CopyTo(destination[position..]);
            position += digitCount;
            destination.Slice(position, pointAt - digitCount).Fill((byte)'0');
            position += pointAt - digitCount;
        }
        else
        {
            digits[..pointAt].CopyTo(destination[position..]);
            position += pointAt;
            destination[position++] = (byte)'.';
            digits[pointAt..digitCount].CopyTo(destination[position..]);
            position += digitCount - pointAt;
        }

        bytesWritten = position;
        return true;
    }

    private static int ReadExponent(ReadOnlySpan<byte> text)
    {
        int sign = 1;
        int index = 0;

        if (text[0] == (byte)'-')
        {
            sign = -1;
            index = 1;
        }
        else if (text[0] == (byte)'+')
        {
            index = 1;
        }

        int value = 0;

        for (; index < text.Length; index++)
        {
            value = value * 10 + (text[index] - '0');
        }

        return sign * value;
    }
}
