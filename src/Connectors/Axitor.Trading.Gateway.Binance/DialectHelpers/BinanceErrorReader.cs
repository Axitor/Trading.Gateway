using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Time;

namespace Axitor.Trading.Gateway.Binance.DialectHelpers;

/// <summary>Reads exchange error code, message and data from a JSON error object.</summary>
internal static class BinanceErrorReader
{
    /// <summary>
    /// Cold path.
    /// Scans a raw UTF-8 REST error body for the exchange error code, message and data.
    /// Allocates strings on the managed heap only for what is captured.
    /// </summary>
    public static void ParseError(
        ReadOnlySpan<byte> body,
        bool captureMessage,
        bool captureData,
        out int code,
        out string? message,
        out string? data,
        out bool isDuplicateOrderMessage)
    {
        code = 0;
        message = null;
        data = null;
        isDuplicateOrderMessage = false;

        if (body.IsEmpty) return;

        try
        {
            Utf8JsonReader reader = new(body);

            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                // REST reads the ban length from the Retry-After header instead.
                ReadErrorObject(ref reader, body, captureMessage, captureData, out code, out message, out data, out isDuplicateOrderMessage, out _);
            }
        }
        catch (JsonException)
        {
            // WAF/Proxy HTML response or malformed/truncated JSON error payload.
        }
    }

    /// <summary>
    /// Cold path.
    /// Reads the error object the reader is on, up to its closing brace.
    /// Allocates strings on the managed heap only for what is captured.
    /// </summary>
    public static void ReadErrorObject(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> source,
        bool captureMessage,
        bool captureData,
        out int code,
        out string? message,
        out string? data,
        out bool isDuplicateOrderMessage,
        out long retryAfterMillis)
    {
        code = 0;
        message = null;
        data = null;
        isDuplicateOrderMessage = false;
        retryAfterMillis = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("code"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.Number)
                {
                    reader.TryGetInt32(out code);
                }
            }
            else if (reader.ValueTextEquals("msg"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.String)
                {
                    isDuplicateOrderMessage = reader.ValueTextEquals(BinanceErrorClassifier.SpotDuplicateOrderMessage);
                    if (captureMessage) message = reader.GetString();
                }
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    ReadErrorData(ref reader, source, captureData, out data, out retryAfterMillis);
                }
            }
            else
            {
                reader.Read();
            }

            // Steps over a value of an unexpected shape.
            reader.Skip();
        }
    }

    /// <summary>Reads the data object the reader is on and works out the ban length from it.</summary>
    private static void ReadErrorData(ref Utf8JsonReader reader, ReadOnlySpan<byte> source, bool captureData, out string? data, out long retryAfterMillis)
    {
        data = null;
        retryAfterMillis = 0;

        int start = (int)reader.TokenStartIndex;
        long serverTime = 0;
        long retryAfter = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("serverTime"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.Number)
                {
                    reader.TryGetInt64(out serverTime);
                }
            }
            else if (reader.ValueTextEquals("retryAfter"u8))
            {
                reader.Read();

                if (reader.TokenType == JsonTokenType.Number)
                {
                    reader.TryGetInt64(out retryAfter);
                }
            }
            else
            {
                reader.Read();
            }

            reader.Skip();
        }

        // retryAfter is when the ban lifts. Measured against the server clock, the local one only when absent.
        if (0 < retryAfter)
        {
            long nowMillis = 0 < serverTime ? serverTime : SysTime.UtcMillis();

            // Never zero: zero reads as absent and brings the long fallback ban.
            retryAfterMillis = Math.Max(1, retryAfter - nowMillis);
        }

        if (captureData)
        {
            data = Encoding.UTF8.GetString(source[start..(int)reader.BytesConsumed]);
        }
    }
}
