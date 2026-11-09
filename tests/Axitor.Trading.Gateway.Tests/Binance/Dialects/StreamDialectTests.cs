using System.Text;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Binance.Dialects;

/// <summary>How the JSON stream dialect splits a frame into route key and data.</summary>
public sealed class StreamDialectTests
{
    private const string Data = """{"e":"bookTicker","u":1}""";

    [Fact]
    public void FindsStreamNameAndDataOfCombinedFrame()
    {
        byte[] message = Encoding.ASCII.GetBytes("""{"stream":"btcusdt@bookTicker","data":""" + Data + "}");

        Assert.Equal(WsInboundKind.Push, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal("btcusdt@bookTicker", Slice(message, inbound.KeyStart, inbound.KeyLength));
        Assert.Equal(Data, Slice(message, inbound.DataStart, inbound.DataLength));
        Assert.Equal(0, inbound.Channel);
        Assert.Equal(WsInboundSignal.None, inbound.Signal);
    }

    [Fact]
    public void ReadsKeysInAnyOrder()
    {
        byte[] message = Encoding.ASCII.GetBytes("""{"data":""" + Data + ""","stream":"btcusdt@bookTicker"}""");

        Assert.Equal(WsInboundKind.Push, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal("btcusdt@bookTicker", Slice(message, inbound.KeyStart, inbound.KeyLength));
        Assert.Equal(Data, Slice(message, inbound.DataStart, inbound.DataLength));
    }

    [Theory]
    [InlineData("""{"stream":"!serverShutdown","data":{"e":"serverShutdown","E":1}}""")]
    [InlineData("""{"data":{"e":"serverShutdown","E":1},"stream":"!serverShutdown"}""")]
    [InlineData("""{"e":"serverShutdown","E":1}""")]
    public void ServerShutdownIsSignal(string text)
    {
        byte[] message = Encoding.ASCII.GetBytes(text);

        Assert.Equal(WsInboundKind.Push, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal(WsInboundSignal.ServerShutdown, inbound.Signal);
        Assert.Equal(0, inbound.KeyLength);
    }

    [Fact]
    public void OtherExchangeStreamIsRoutedByName()
    {
        byte[] message = """{"stream":"!contractInfo","data":{"e":"contractInfo"}}"""u8.ToArray();

        Assert.Equal(WsInboundKind.Push, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal("!contractInfo", Slice(message, inbound.KeyStart, inbound.KeyLength));
        Assert.Equal(WsInboundSignal.None, inbound.Signal);
    }

    [Fact]
    public void ExchangeEventGoesWhole()
    {
        byte[] message = """{"e":"listenKeyExpired","E":1576653824250,"listenKey":"k"}"""u8.ToArray();

        Assert.Equal(WsInboundKind.Push, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal(0, inbound.KeyLength);
        Assert.Equal(0, inbound.DataStart);
        Assert.Equal(message.Length, inbound.DataLength);
        Assert.Equal(WsInboundSignal.None, inbound.Signal);
    }

    [Theory]
    [InlineData("""{"result":null,"id":7}""", false, 0)]
    [InlineData("""{"code":2,"msg":"Invalid request","id":7}""", true, 2)]
    [InlineData("""{"error":{"code":2,"msg":"Invalid request"},"id":7}""", true, 2)]
    public void ReadsControlReply(string text, bool isError, int code)
    {
        byte[] message = Encoding.ASCII.GetBytes(text);

        Assert.Equal(WsInboundKind.Response, BinanceDialectWsStream.ReadInbound(message, isBinary: false, out WsStreamInbound inbound));
        Assert.Equal(7, inbound.RequestId);
        Assert.Equal(isError, inbound.IsError);
        Assert.Equal(code, inbound.ExchangeCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"foo":1}""")]
    [InlineData("""{"stream":"btcusdt@depth","data":}""")]
    public void MalformedMessageIsUnknown(string text) =>
        Assert.Equal(WsInboundKind.Unknown, BinanceDialectWsStream.ReadInbound(Encoding.ASCII.GetBytes(text), isBinary: false, out _));

    [Fact]
    public void BinaryFrameIsUnknown()
    {
        byte[] message = Encoding.ASCII.GetBytes("""{"stream":"btcusdt@bookTicker","data":""" + Data + "}");

        Assert.Equal(WsInboundKind.Unknown, BinanceDialectWsStream.ReadInbound(message, isBinary: true, out _));
    }

    private static string Slice(byte[] message, int start, int length) => Encoding.ASCII.GetString(message, start, length);
}
