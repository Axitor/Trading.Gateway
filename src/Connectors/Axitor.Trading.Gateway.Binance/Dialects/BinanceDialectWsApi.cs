using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.DialectHelpers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Binance.Dialects;

/// <summary>Everything the engine has to know about Binance WebSocket API specifically.</summary>
internal readonly struct BinanceDialectWsApi : IExchangeDialectWsApi
{
    /// <summary>A response carries an id, an event does not. Our ids are numbers, so a null or text id reads as zero. Runs on the receive loop.</summary>
    public static WsInboundKind ReadInbound(ReadOnlySpan<byte> message, out long requestId)
    {
        requestId = 0;

        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return WsInboundKind.Unknown;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        reader.TryGetInt64(out requestId);
                    }

                    return WsInboundKind.Response;
                }

                if (reader.ValueTextEquals("event"u8))
                {
                    return WsInboundKind.Push;
                }

                reader.Read();
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // A malformed message is dropped as unknown, never turned into a handler fault.
        }

        return WsInboundKind.Unknown;
    }

    /// <summary>Writes the request envelope up to where its params begin.</summary>
    public static void WriteRequestStart(ref PayloadBuilder builder, long requestId, ReadOnlySpan<byte> method)
    {
        builder.Append("{\"id\":"u8);

        if (!requestId.TryFormat(builder.FreeSpan(), out int written, default, CultureInfo.InvariantCulture))
        {
            ThrowBufferTooSmall();
        }

        builder.Advance(written);
        builder.Append(",\"method\":\""u8);
        builder.Append(method);
        builder.Append("\",\"params\":{"u8);
    }

    /// <summary>Closes the params and the envelope.</summary>
    public static void WriteRequestEnd(ref PayloadBuilder builder) => builder.Append("}}"u8);

    /// <summary>Reads the response envelope and hands its rate limits to the limiter. Allocates strings only for what is captured.</summary>
    public static void ReadResponse<TLimiter>(TLimiter limiter, ReadOnlySpan<byte> message, int market, bool captureMessage, bool captureData, out WsApiResponseEnvelope envelope)
        where TLimiter : IRateLimiter
    {
        BinanceMarkets binanceMarket = (BinanceMarkets)market;

        if (!BinanceWsApiResponseReader.TryRead(limiter, message, binanceMarket, captureMessage, captureData, out envelope, out bool isDuplicateOrderMessage))
        {
            envelope.Reason = WsApiRequestErrorReason.ExceptionJson;
            return;
        }

        if (envelope.StatusCode == 200)
        {
            return;
        }

        BinanceErrorKind kind = BinanceErrorClassifier.Classify(envelope.StatusCode, envelope.ExchangeCode, isDuplicateOrderMessage, binanceMarket);
        envelope.Reason = ToReason(kind);
    }

    /// <summary>Cold path. Classifies an exchange error code to resiliency action.</summary>
    public static RequestErrorAction ClassifyExchangeCodeAction(int exchangeCode, int market, bool isRetryable) =>
        BinanceErrorClassifier.ClassifyExchangeCodeAction(exchangeCode, (BinanceMarkets)market, isRetryable);

    /// <summary>Cold path. Maps the status the way the error classifier does and reads Retry-After in seconds.</summary>
    public static void ReadHandshakeFailure(int statusCode, IReadOnlyDictionary<string, IEnumerable<string>>? headers, out WsDisconnectReason reason, out int retryAfterSeconds) =>
        BinanceHandshakeReader.ReadHandshakeFailure(statusCode, headers, out reason, out retryAfterSeconds);

    private static WsApiRequestErrorReason ToReason(BinanceErrorKind kind) =>
        kind switch
        {
            BinanceErrorKind.Unauthorized => WsApiRequestErrorReason.ExchangeApiUnauthorized,
            BinanceErrorKind.BanLong => WsApiRequestErrorReason.ExchangeApiBanLong,
            BinanceErrorKind.BanShort => WsApiRequestErrorReason.ExchangeApiBanShort,
            BinanceErrorKind.NotFound => WsApiRequestErrorReason.StatusCodeNotFound,
            BinanceErrorKind.Malformed => WsApiRequestErrorReason.ExchangeApiRequestMalformed,
            BinanceErrorKind.Partial => WsApiRequestErrorReason.ExchangeApiRequestPartial,
            BinanceErrorKind.AlreadyPlaced => WsApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced,
            BinanceErrorKind.TimestampRejected => WsApiRequestErrorReason.ExchangeApiTimestampRejected,
            BinanceErrorKind.StatusUnknown => WsApiRequestErrorReason.ExchangeApiRequestStatusUnknown,
            BinanceErrorKind.RequestFailed => WsApiRequestErrorReason.ExchangeApiRequestFailed,
            BinanceErrorKind.RequestError => WsApiRequestErrorReason.ExchangeApiRequestError,
            _ => WsApiRequestErrorReason.StatusCodeOtherError,
        };

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBufferTooSmall() => throw new InvalidOperationException("Request buffer is too small for this endpoint.");
}
