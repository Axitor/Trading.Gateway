using System.Text;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Tests.Core.Primitives;

public sealed class ClientOrderIdTests
{
    [Theory]
    [InlineData("my-order-1")]
    [InlineData("x.y:z/w_1")]
    [InlineData("autoclose-1700000000000")]
    [InlineData("AbCdEf")]
    [InlineData("A")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    public void KeepsTheIdAsGiven(string text)
    {
        Assert.True(ClientOrderId.TryParse(text, out ClientOrderId fromChars));
        Assert.True(ClientOrderId.TryParse(Encoding.ASCII.GetBytes(text), out ClientOrderId fromBytes));

        Assert.Equal(fromChars, fromBytes);
        Assert.Equal(fromChars.GetHashCode(), fromBytes.GetHashCode());
        Assert.Equal(text, fromChars.ToString());
        Assert.Equal(text.Length, fromChars.Length);
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789a")]
    [InlineData("my order")]
    [InlineData("a+b")]
    [InlineData("a@b")]
    [InlineData("a\u00E9b")]
    public void RejectsTextOutsideTheIdRules(string text)
    {
        Assert.False(ClientOrderId.TryParse(text, out _));
        Assert.False(ClientOrderId.TryParse(Encoding.UTF8.GetBytes(text), out _));
        Assert.Throws<ArgumentException>(() => ClientOrderId.Parse(text));
    }

    [Fact]
    public void EmptyTextIsTheEmptyId()
    {
        Assert.True(ClientOrderId.TryParse(string.Empty, out ClientOrderId id));

        Assert.True(id.IsEmpty);
        Assert.Equal(ClientOrderId.Empty, id);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void CompareIsCaseSensitive() =>
        Assert.True(ClientOrderId.Parse("AbC") != ClientOrderId.Parse("abc"));

    [Fact]
    public void WritesAllOrNothing()
    {
        ClientOrderId id = ClientOrderId.Parse("my-order-1");
        byte[] exact = new byte[10];
        byte[] shorter = new byte[9];

        Assert.True(id.TryWriteTo(exact, out int exactWritten));
        Assert.False(id.TryWriteTo(shorter, out int shorterWritten));

        Assert.Equal(10, exactWritten);
        Assert.Equal("my-order-1"u8.ToArray(), exact);
        Assert.Equal(0, shorterWritten);
        Assert.Equal(new byte[9], shorter);
    }
}
