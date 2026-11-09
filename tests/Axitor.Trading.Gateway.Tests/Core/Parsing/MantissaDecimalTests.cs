using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Tests.Core.Parsing;

/// <summary>A price from a binary stream must land on the same double as the same price from a JSON snapshot.</summary>
public sealed class MantissaDecimalTests
{
    private const int FuzzSeed = 20261031;
    private const int FuzzCases = 100_000;

    [Fact]
    public void TrailingZerosDoNotChangeTheDouble()
    {
        double shortForm = MantissaDecimal.ToDouble(6700001, -2);
        double longForm = MantissaDecimal.ToDouble(6700001000000, -8);

        Assert.Equal(Bits(67000.01), Bits(shortForm));
        Assert.Equal(Bits(shortForm), Bits(longForm));
    }

    [Theory]
    [InlineData(6700001L, -2)]
    [InlineData(-15L, -1)]
    [InlineData(0L, -8)]
    [InlineData(123L, 0)]
    [InlineData(9007199254740992L, -8)]
    [InlineData(9007199254740993L, -8)]
    [InlineData(-9007199254740992L, -2)]
    [InlineData(-9007199254740993L, -2)]
    [InlineData(1L, -22)]
    [InlineData(1L, -23)]
    [InlineData(1L, 22)]
    [InlineData(1L, 23)]
    [InlineData(long.MaxValue, -8)]
    [InlineData(long.MinValue, -8)]
    public void GivesTheSameDoubleAsTheDecimalText(long mantissa, int exponent)
    {
        Assert.True(FastDecimalParser.TryParse(PlainText(mantissa, exponent), out double expected, out _));
        Assert.Equal(Bits(expected), Bits(MantissaDecimal.ToDouble(mantissa, exponent)));
    }

    [Fact]
    public void MatchesTheDecimalTextOnRandomValues()
    {
        Random random = new(FuzzSeed);

        for (int i = 0; i < FuzzCases; i++)
        {
            long mantissa = random.Next(3) switch
            {
                0 => random.NextInt64(-1_000_000_000, 1_000_000_000),
                1 => random.NextInt64(-(1L << 53), (1L << 53) + 1),
                _ => random.NextInt64(long.MinValue, long.MaxValue),
            };

            int exponent = random.Next(-25, 6);

            bool parsed = FastDecimalParser.TryParse(PlainText(mantissa, exponent), out double expected, out _);
            double actual = MantissaDecimal.ToDouble(mantissa, exponent);

            if (!parsed || Bits(expected) != Bits(actual))
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{mantissa}E{exponent}: text parsed {parsed}, expected {expected:R}, got {actual:R}."));
            }
        }
    }

    /// <summary>The number as an exchange writes it in JSON: plain digits, no exponent.</summary>
    private static byte[] PlainText(long mantissa, int exponent)
    {
        string digits = mantissa.ToString(CultureInfo.InvariantCulture).TrimStart('-');
        string sign = mantissa < 0 ? "-" : string.Empty;
        string body;

        if (0 <= exponent)
        {
            body = digits + new string('0', exponent);
        }
        else
        {
            int places = -exponent;

            body = places < digits.Length
                ? string.Concat(digits.AsSpan(0, digits.Length - places), ".", digits.AsSpan(digits.Length - places))
                : string.Concat("0.", new string('0', places - digits.Length), digits);
        }

        return Encoding.ASCII.GetBytes(sign + body);
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
