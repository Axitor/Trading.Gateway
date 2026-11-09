using System.Globalization;
using System.Text;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Tests.Core.Primitives;

public sealed class SymbolTests
{
    private const int FuzzSeed = 20261031;
    private const int FuzzCases = 20_000;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_./";

    [Theory]
    [InlineData("BTCUSDT")]
    [InlineData("btcusdt")]
    [InlineData("BTCUSD_PERP")]
    [InlineData("1000PEPEUSDT")]
    [InlineData("ETH-BTC")]
    [InlineData("BTC/USDT")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void KeepsTheTickerAsGiven(string text)
    {
        Assert.True(Symbol.TryParse(text, out Symbol fromChars));
        Assert.True(Symbol.TryParse(Encoding.ASCII.GetBytes(text), out Symbol fromBytes));

        Assert.Equal(fromChars, fromBytes);
        Assert.Equal(text, fromChars.ToString());
        Assert.Equal(text.Length, fromChars.Length);
    }

    [Theory]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    [InlineData("BTC USDT")]
    [InlineData("BTC@USDT")]
    [InlineData("BTC\0USDT")]
    [InlineData("BTC\u00DCSDT")]
    public void RejectsTextOutsideTheTickerRules(string text)
    {
        Assert.False(Symbol.TryParse(text, out _));
        Assert.False(Symbol.TryParse(Encoding.UTF8.GetBytes(text), out _));
        Assert.Throws<ArgumentException>(() => Symbol.Parse(text));
    }

    [Fact]
    public void EmptyTextIsTheEmptySymbol()
    {
        Assert.True(Symbol.TryParse(string.Empty, out Symbol symbol));

        Assert.True(symbol.IsEmpty);
        Assert.Equal(Symbol.Empty, symbol);
        Assert.Equal(0, symbol.Length);
        Assert.Equal(string.Empty, symbol.ToString());
    }

    [Fact]
    public void OrdersLikeAnOrdinalStringCompare()
    {
        Random random = new(FuzzSeed);

        for (int i = 0; i < FuzzCases; i++)
        {
            string left = NextTicker(random);

            // Every fourth pair is a ticker and its own prefix.
            string right = random.Next(4) == 0 ? left[..random.Next(1, left.Length + 1)] : NextTicker(random);

            int expected = Math.Sign(string.CompareOrdinal(left, right));
            int actual = Math.Sign(Symbol.Parse(left).CompareTo(Symbol.Parse(right)));

            if (expected != actual)
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{left} vs {right}: expected {expected}, got {actual}."));
            }
        }
    }

    [Fact]
    public void WritesAllOrNothing()
    {
        Symbol symbol = Symbol.Parse("BTCUSDT");
        byte[] exact = new byte[7];
        byte[] shorter = new byte[6];

        Assert.True(symbol.TryWriteTo(exact, out int exactWritten));
        Assert.False(symbol.TryWriteTo(shorter, out int shorterWritten));

        Assert.Equal(7, exactWritten);
        Assert.Equal("BTCUSDT"u8.ToArray(), exact);
        Assert.Equal(0, shorterWritten);
        Assert.Equal(new byte[6], shorter);
    }

    private static string NextTicker(Random random)
    {
        Span<char> chars = stackalloc char[Symbol.MaxLength];
        int length = random.Next(1, Symbol.MaxLength + 1);

        for (int i = 0; i < length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(chars[..length]);
    }
}
