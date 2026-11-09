using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Parsing.Sbe;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Dialects;

/// <summary>Everything the engine has to know about the Binance spot SBE streams specifically.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Market data comes in binary frames with no envelope and no stream name, so it is routed by the template id and the symbol
/// at the end of the message. Subscribe replies and serverShutdown come as JSON in text frames and are read as on the JSON streams.
/// </para>
/// <para>
/// DESIGN NOTE: A newer schema version that appends fields still routes and parses. One that adds a group no longer finds its symbol,
/// so its messages are reported as unknown rather than misread.
/// </para>
/// </remarks>
internal readonly struct BinanceDialectSbeStream : IExchangeDialectWsStream
{
    /// <summary>Routes market data by template id and symbol; reads text frames as the JSON streams do. Runs on the receive loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WsInboundKind ReadInbound(ReadOnlySpan<byte> message, bool isBinary, out WsStreamInbound inbound)
    {
        if (!isBinary)
        {
            return BinanceDialectWsStream.ReadInbound(message, isBinary: false, out inbound);
        }

        if (!BinanceSbeReader.TryFindSymbol(message, out int templateId, out int symbolStart, out int symbolLength))
        {
            inbound = default;
            return WsInboundKind.Unknown;
        }

        inbound = new WsStreamInbound()
        {
            Channel = templateId,
            KeyStart = symbolStart,
            KeyLength = symbolLength,
            DataStart = 0,
            DataLength = message.Length,
        };

        return WsInboundKind.Push;
    }

    /// <summary>Writes a subscribe or unsubscribe request up to its first stream name. Same JSON as the JSON streams.</summary>
    public static void WriteControlStart(ref PayloadBuilder builder, long requestId, bool subscribe) =>
        BinanceDialectWsStream.WriteControlStart(ref builder, requestId, subscribe);

    /// <summary>Writes one stream name into the request.</summary>
    public static void WriteControlName(ref PayloadBuilder builder, ReadOnlySpan<byte> name, bool isFirst) =>
        BinanceDialectWsStream.WriteControlName(ref builder, name, isFirst);

    /// <summary>Closes the request.</summary>
    public static void WriteControlEnd(ref PayloadBuilder builder) => BinanceDialectWsStream.WriteControlEnd(ref builder);

    /// <summary>Cold path. Resiliency action for an error reply to a control request.</summary>
    public static RequestErrorAction ClassifyControlError(int exchangeCode) => BinanceDialectWsStream.ClassifyControlError(exchangeCode);

    /// <summary>Cold path. Maps the status the way the error classifier does and reads Retry-After in seconds.</summary>
    public static void ReadHandshakeFailure(int statusCode, IReadOnlyDictionary<string, IEnumerable<string>>? headers, out WsDisconnectReason reason, out int retryAfterSeconds) =>
        BinanceDialectWsStream.ReadHandshakeFailure(statusCode, headers, out reason, out retryAfterSeconds);
}
