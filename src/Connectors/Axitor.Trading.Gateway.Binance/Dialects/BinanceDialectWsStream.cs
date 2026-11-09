using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.DialectHelpers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Dialects;

/// <summary>Everything the engine has to know about Binance WebSocket streams specifically. Always the combined stream mode.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Stream data is almost every message, and the exchange always writes the stream name first.
/// So it is found by a fixed prefix and two byte searches, with no JSON reader.
/// Anything else goes to a slow path with a full reader: control replies, exchange events, any other key order.
/// </para>
/// <para>
/// DESIGN NOTE: The docs show the error reply only as code and msg. Both shapes are read:
/// code at the top level, or inside an error object.
/// </para>
/// <para>
/// DESIGN NOTE: Spot announces a restart with the !serverShutdown stream ten minutes before it drops the connection,
/// and the shard replaces the connection make-before-break. Exchange stream names start with '!', so the fast path pays one byte check.
/// </para>
/// </remarks>
internal readonly struct BinanceDialectWsStream : IExchangeDialectWsStream
{
    // Combined stream envelope: {"stream":"<name>","data":<payload>}
    private static ReadOnlySpan<byte> StreamPrefix => "{\"stream\":\""u8;
    private static ReadOnlySpan<byte> DataSeparator => "\",\"data\":"u8;

    // Server shutdown: the stream name in combined mode, the event type in raw mode.
    private static ReadOnlySpan<byte> ServerShutdownStream => "!serverShutdown"u8;
    private static ReadOnlySpan<byte> ServerShutdownEvent => "serverShutdown"u8;

    /// <summary>Tells stream data, an exchange event and a control reply apart, and finds their bounds. Data is routed by its stream name. Runs on the receive loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WsInboundKind ReadInbound(ReadOnlySpan<byte> message, bool isBinary, out WsStreamInbound inbound)
    {
        // JSON streams never send binary frames.
        if (isBinary)
        {
            inbound = default;
            return WsInboundKind.Unknown;
        }

        // HOT: stream data, no JSON reader.
        if (message.StartsWith(StreamPrefix))
        {
            int nameStart = StreamPrefix.Length;
            int nameLength = message[nameStart..].IndexOf((byte)'"');
            int nameEnd = nameStart + nameLength;
            int dataStart = nameEnd + DataSeparator.Length;

            // Empty data goes to the slow path and reads as unknown: a parser never gets an empty span.
            if (0 < nameLength && message[nameEnd..].StartsWith(DataSeparator) && dataStart < message.Length - 1 && message[^1] == (byte)'}')
            {
                if (message[nameStart] == (byte)'!')
                {
                    return ReadExchangeStream(message, nameStart, nameLength, dataStart, out inbound);
                }

                inbound = new WsStreamInbound()
                {
                    KeyStart = nameStart,
                    KeyLength = nameLength,
                    DataStart = dataStart,
                    DataLength = message.Length - 1 - dataStart,
                };

                return WsInboundKind.Push;
            }
        }

        return ReadInboundSlow(message, out inbound);
    }

    /// <summary>Writes a subscribe or unsubscribe request up to its first stream name.</summary>
    public static void WriteControlStart(ref PayloadBuilder builder, long requestId, bool subscribe)
    {
        builder.Append("{\"id\":"u8);

        if (!requestId.TryFormat(builder.FreeSpan(), out int written, default, CultureInfo.InvariantCulture))
        {
            ThrowBufferTooSmall();
        }

        builder.Advance(written);
        builder.Append(subscribe ? ",\"method\":\"SUBSCRIBE\",\"params\":["u8 : ",\"method\":\"UNSUBSCRIBE\",\"params\":["u8);
    }

    /// <summary>Writes one stream name into the request.</summary>
    public static void WriteControlName(ref PayloadBuilder builder, ReadOnlySpan<byte> name, bool isFirst)
    {
        if (!isFirst)
        {
            builder.Append(","u8);
        }

        builder.Append("\""u8);
        builder.Append(name);
        builder.Append("\""u8);
    }

    /// <summary>Closes the request.</summary>
    public static void WriteControlEnd(ref PayloadBuilder builder) => builder.Append("]}"u8);

    /// <summary>Cold path. Resiliency action for an error reply to a control request.</summary>
    public static RequestErrorAction ClassifyControlError(int exchangeCode) =>
        exchangeCode switch
        {
            // Every documented code means our request was malformed: a bug that retrying cannot fix.
            // 0: Unknown property.
            // 1: Invalid value type: expected Boolean.
            // 2: Invalid request: bad id type, unknown method, too many parameters or a missing field.
            // 3: Invalid JSON.
            0 or 1 or 2 or 3 => RequestErrorAction.Fatal,

            // An undocumented code: the subscription is refused, the transport keeps going.
            _ => RequestErrorAction.Skip,
        };

    /// <summary>Cold path. Maps the status the way the error classifier does and reads Retry-After in seconds.</summary>
    public static void ReadHandshakeFailure(int statusCode, IReadOnlyDictionary<string, IEnumerable<string>>? headers, out WsDisconnectReason reason, out int retryAfterSeconds) =>
        BinanceHandshakeReader.ReadHandshakeFailure(statusCode, headers, out reason, out retryAfterSeconds);

    /// <summary>Cold path. A stream the exchange pushes on its own. A shutdown becomes a signal; any other one is routed by name and reported as unknown.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WsInboundKind ReadExchangeStream(ReadOnlySpan<byte> message, int nameStart, int nameLength, int dataStart, out WsStreamInbound inbound)
    {
        if (message.Slice(nameStart, nameLength).SequenceEqual(ServerShutdownStream))
        {
            inbound = ServerShutdownInbound(message.Length);
            return WsInboundKind.Push;
        }

        inbound = new WsStreamInbound()
        {
            KeyStart = nameStart,
            KeyLength = nameLength,
            DataStart = dataStart,
            DataLength = message.Length - 1 - dataStart,
        };

        return WsInboundKind.Push;
    }

    /// <summary>Cold path. Reads a message the fast path did not recognize.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WsInboundKind ReadInboundSlow(ReadOnlySpan<byte> message, out WsStreamInbound inbound)
    {
        inbound = default;
        bool hasId = false;
        bool hasData = false;
        bool hasEvent = false;
        bool isShutdown = false;

        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return WsInboundKind.Unknown;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("stream"u8))
                {
                    reader.Read();

                    // Stream names are plain ASCII, so an escaped name is not ours.
                    if (reader.TokenType == JsonTokenType.String && !reader.ValueIsEscaped)
                    {
                        inbound.KeyStart = (int)reader.TokenStartIndex + 1;
                        inbound.KeyLength = reader.ValueSpan.Length;
                    }
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    reader.Read();
                    inbound.DataStart = (int)reader.TokenStartIndex;
                    reader.Skip();
                    inbound.DataLength = (int)reader.BytesConsumed - inbound.DataStart;
                    hasData = true;
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    hasId = reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out inbound.RequestId);
                }
                else if (reader.ValueTextEquals("code"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out inbound.ExchangeCode))
                    {
                        inbound.IsError = true;
                    }
                }
                else if (reader.ValueTextEquals("error"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadErrorObject(ref reader, ref inbound);
                    }
                }
                else if (reader.ValueTextEquals("e"u8))
                {
                    reader.Read();
                    hasEvent = true;
                    isShutdown = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(ServerShutdownEvent);
                }
                else
                {
                    reader.Read();
                }

                // Steps over a value of an unexpected shape.
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // A malformed message is dropped as unknown, never turned into a handler fault.
            inbound = default;
            return WsInboundKind.Unknown;
        }

        if (hasId)
        {
            return WsInboundKind.Response;
        }

        if (0 < inbound.KeyLength && hasData)
        {
            // The shutdown stream with its keys in another order.
            if (message.Slice(inbound.KeyStart, inbound.KeyLength).SequenceEqual(ServerShutdownStream))
            {
                inbound = ServerShutdownInbound(message.Length);
            }

            return WsInboundKind.Push;
        }

        // An exchange event outside any subscription: the whole message goes to the dispatcher.
        if (hasEvent)
        {
            inbound = isShutdown
                ? ServerShutdownInbound(message.Length)
                : new WsStreamInbound() { DataStart = 0, DataLength = message.Length };

            return WsInboundKind.Push;
        }

        inbound = default;
        return WsInboundKind.Unknown;
    }

    /// <summary>An exchange event that tells the core to replace the connection.</summary>
    private static WsStreamInbound ServerShutdownInbound(int messageLength) =>
        new() { DataStart = 0, DataLength = messageLength, Signal = WsInboundSignal.ServerShutdown };

    /// <summary>Reads the code from the error object the reader is on, up to its closing brace.</summary>
    private static void ReadErrorObject(ref Utf8JsonReader reader, ref WsStreamInbound inbound)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("code"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out inbound.ExchangeCode))
                {
                    inbound.IsError = true;
                }
            }
            else
            {
                reader.Read();
            }

            reader.Skip();
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBufferTooSmall() => throw new InvalidOperationException("Request buffer is too small for this request.");
}
