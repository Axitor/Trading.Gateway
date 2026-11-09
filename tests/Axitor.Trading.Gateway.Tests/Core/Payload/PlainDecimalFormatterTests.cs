using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Tests.Core.Payload;

/// <summary>Exchanges reject exponent form, so prices and quantities go out as plain decimals.</summary>
public sealed class PlainDecimalFormatterTests
{
    private const int FuzzSeed = 20261031;
    private const int FuzzCases = 100_000;
    private const int BufferBytes = 64;

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(60000d, "60000")]
    [InlineData(0.01, "0.01")]
    [InlineData(12345.6789, "12345.6789")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(1e-7, "0.0000001")]
    [InlineData(-1e-7, "-0.0000001")]
    [InlineData(1.5e-8, "0.000000015")]
    [InlineData(1.2e-5, "0.000012")]
    [InlineData(1e15, "1000000000000000")]
    [InlineData(1e21, "1000000000000000000000")]
    [InlineData(123456789012345d, "123456789012345")]
    [InlineData(1234567890123456d, "1234567890123460")]
    [InlineData(1.23456789012345678, "1.23456789012346")]
    public void WritesPlainDecimalText(double value, string expected) => Assert.Equal(expected, Format(value));

    [Fact]
    public void DropsBinaryNoise() => Assert.Equal("0.3", Format(0.1 + 0.2));

    [Fact]
    public void WritesNothingIntoAShortBuffer()
    {
        Assert.False(PlainDecimalFormatter.TryFormat(60000, new byte[4], out int plainWritten));
        Assert.False(PlainDecimalFormatter.TryFormat(1e-7, new byte[8], out int expandedWritten));

        Assert.Equal(0, plainWritten);
        Assert.Equal(0, expandedWritten);
    }

    [Fact]
    public void RoundTripsToFifteenSignificantDigits()
    {
        Random random = new(FuzzSeed);

        for (int i = 0; i < FuzzCases; i++)
        {
            long mantissa = random.NextInt64(-999_999_999_999_999, 1_000_000_000_000_000);
            int exponent = random.Next(-20, 8);
            double value = double.Parse(string.Create(CultureInfo.InvariantCulture, $"{mantissa}E{exponent}"), CultureInfo.InvariantCulture);

            string text = Format(value);
            double expected = double.Parse(value.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            double actual = double.Parse(text, CultureInfo.InvariantCulture);

            if (0 <= text.AsSpan().IndexOfAny('E', 'e') || Bits(expected) != Bits(actual))
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{value:R}: wrote {text}, expected the value of {expected:R}."));
            }
        }
    }

    private static string Format(double value)
    {
        byte[] buffer = new byte[BufferBytes];

        Assert.True(PlainDecimalFormatter.TryFormat(value, buffer, out int written));
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
