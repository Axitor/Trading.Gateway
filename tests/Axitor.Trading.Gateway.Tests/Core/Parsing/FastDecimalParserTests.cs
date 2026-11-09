using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Tests.Core.Parsing;

public sealed class FastDecimalParserTests
{
    private const int FuzzSeed = 20261031;
    private const int FuzzCases = 200_000;

    // Past 19 digits or 22 places a number goes to the full parser, so the fuzz covers both paths.
    private const int FuzzMaxDigits = 21;
    private const int FuzzMaxPlaces = 25;

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("0.0")]
    [InlineData("1")]
    [InlineData("60000")]
    [InlineData("0.1")]
    [InlineData("0.3")]
    [InlineData("67000.01")]
    [InlineData("-123.456")]
    [InlineData("0.00000001")]
    [InlineData("000123.4500")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("9007199254740992")]
    [InlineData("9007199254740993")]
    [InlineData("0.0000000000000000000001")]
    [InlineData("0.00000000000000000000001")]
    [InlineData("1234567890123456789")]
    [InlineData("12345678901234567890")]
    [InlineData("0.30000000000000004")]
    [InlineData("1e-7")]
    [InlineData("1.5E+3")]
    public void ParsesToTheSameDoubleAsTheFullParser(string text)
    {
        byte[] utf8 = Encoding.ASCII.GetBytes(text);

        Assert.True(FastDecimalParser.TryParse(utf8, out double value, out int consumed));
        Assert.Equal(utf8.Length, consumed);
        Assert.Equal(Bits(double.Parse(text, CultureInfo.InvariantCulture)), Bits(value));
    }

    [Theory]
    [InlineData("67000.01\",", 8)]
    [InlineData("-0.5]", 4)]
    [InlineData("1e-7\"", 4)]
    public void StopsRightAfterTheNumber(string text, int expectedConsumed)
    {
        Assert.True(FastDecimalParser.TryParse(Encoding.ASCII.GetBytes(text), out double value, out int consumed));
        Assert.Equal(expectedConsumed, consumed);
        Assert.Equal(Bits(double.Parse(text.AsSpan(0, expectedConsumed), CultureInfo.InvariantCulture)), Bits(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("\"1\"")]
    public void FindsNoNumberWhereNoneStarts(string text) =>
        Assert.False(FastDecimalParser.TryParse(Encoding.ASCII.GetBytes(text), out _, out _));

    [Fact]
    public void MatchesTheFullParserOnRandomDecimals()
    {
        Random random = new(FuzzSeed);
        char[] digits = new char[FuzzMaxDigits];

        for (int i = 0; i < FuzzCases; i++)
        {
            string text = NextDecimal(random, digits);
            byte[] utf8 = Encoding.ASCII.GetBytes(text);
            double expected = double.Parse(text, CultureInfo.InvariantCulture);

            bool parsed = FastDecimalParser.TryParse(utf8, out double value, out int consumed);

            if (!parsed || consumed != utf8.Length || Bits(expected) != Bits(value))
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{text}: expected {expected:R}, got {value:R}, consumed {consumed} of {utf8.Length}."));
            }
        }
    }

    /// <summary>A plain decimal such as -0.000123 or 4512.30, the way exchanges write prices.</summary>
    private static string NextDecimal(Random random, char[] digits)
    {
        int count = random.Next(1, FuzzMaxDigits + 1);
        int places = random.Next(0, FuzzMaxPlaces + 1);

        for (int i = 0; i < count; i++)
        {
            digits[i] = (char)('0' + random.Next(10));
        }

        string all = new(digits, 0, count);

        string body = places == 0 ? all
            : places < count ? string.Concat(all.AsSpan(0, count - places), ".", all.AsSpan(count - places))
            : string.Concat("0.", new string('0', places - count), all);

        return random.Next(4) == 0 ? "-" + body : body;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
