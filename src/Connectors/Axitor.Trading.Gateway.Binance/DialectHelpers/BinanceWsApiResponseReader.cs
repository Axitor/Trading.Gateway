using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Binance.DialectHelpers;

/// <summary>Reads a WebSocket API response envelope in one pass. Runs on the caller, not the receive loop.</summary>
internal static class BinanceWsApiResponseReader
{
    /// <summary>Reads status, result bounds, error and rate limits. False when the envelope is malformed or has no status.</summary>
    public static bool TryRead<TLimiter>(
        TLimiter limiter,
        ReadOnlySpan<byte> message,
        BinanceMarkets market,
        bool captureMessage,
        bool captureData,
        out WsApiResponseEnvelope envelope,
        out bool isDuplicateOrderMessage)
        where TLimiter : IRateLimiter
    {
        envelope = default;
        isDuplicateOrderMessage = false;
        bool hasStatus = false;

        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("status"u8))
                {
                    reader.Read();
                    hasStatus = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out envelope.StatusCode);
                }
                else if (reader.ValueTextEquals("result"u8))
                {
                    reader.Read();
                    envelope.ResultStart = (int)reader.TokenStartIndex;
                    reader.Skip();
                    envelope.ResultLength = (int)reader.BytesConsumed - envelope.ResultStart;
                }
                else if (reader.ValueTextEquals("error"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        BinanceErrorReader.ReadErrorObject(ref reader, message, captureMessage, captureData,
                            out envelope.ExchangeCode, out envelope.ExchangeMessage, out envelope.ExchangeData,
                            out isDuplicateOrderMessage, out envelope.RetryAfterMillis);
                    }
                }
                else if (reader.ValueTextEquals("rateLimits"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        ReadRateLimits(limiter, ref reader, market);
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
        catch (JsonException)
        {
            return false;
        }

        return hasStatus;
    }

    /// <summary>Raises the limiter windows to the usage the exchange reported.</summary>
    private static void ReadRateLimits<TLimiter>(TLimiter limiter, ref Utf8JsonReader reader, BinanceMarkets market)
        where TLimiter : IRateLimiter
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            bool isWeight = false;
            bool isOrders = false;
            long unitMillis = 0;
            int intervalNum = 0;
            int count = 0;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("rateLimitType"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        isWeight = reader.ValueTextEquals("REQUEST_WEIGHT"u8);
                        isOrders = reader.ValueTextEquals("ORDERS"u8);
                    }
                }
                else if (reader.ValueTextEquals("interval"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        unitMillis = ReadIntervalUnitMillis(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("intervalNum"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        reader.TryGetInt32(out intervalNum);
                    }
                }
                else if (reader.ValueTextEquals("count"u8))
                {
                    reader.Read();

                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        reader.TryGetInt32(out count);
                    }
                }
                else
                {
                    reader.Read();
                }

                reader.Skip();
            }

            BinanceRateLimitBucket bucket = ToBucket(isWeight, isOrders, unitMillis * intervalNum, market);

            if (bucket != BinanceRateLimitBucket.None)
            {
                limiter.UpdateWeights((int)bucket, count);
            }
        }
    }

    private static long ReadIntervalUnitMillis(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("SECOND"u8)) return SysTime.MillisPerSecond();
        if (reader.ValueTextEquals("MINUTE"u8)) return SysTime.MillisPerMinute();
        if (reader.ValueTextEquals("DAY"u8)) return SysTime.MillisPerDay();

        return 0;
    }

    /// <summary>The limiter window a reported limit maps to, or None when no window tracks it.</summary>
    private static BinanceRateLimitBucket ToBucket(bool isWeight, bool isOrders, long windowMillis, BinanceMarkets market) =>
        market switch
        {
            BinanceMarkets.FuturesUsdM => ToBucketFuturesUsdM(isWeight, isOrders, windowMillis),
            BinanceMarkets.FuturesCoinM => ToBucketFuturesCoinM(isWeight, isOrders, windowMillis),
            BinanceMarkets.Spot => ToBucketSpot(isWeight, isOrders, windowMillis),
            _ => BinanceRateLimitBucket.None,
        };

    private static BinanceRateLimitBucket ToBucketFuturesUsdM(bool isWeight, bool isOrders, long windowMillis)
    {
        if (isWeight && windowMillis == SysTime.MillisPerMinute()) return BinanceRateLimitBucket.FuturesWsApiRequestWeight;
        if (isOrders && windowMillis == SysTime.MillisPerTenSeconds()) return BinanceRateLimitBucket.FuturesOrderTenSeconds;
        if (isOrders && windowMillis == SysTime.MillisPerMinute()) return BinanceRateLimitBucket.FuturesOrderMinute;

        return BinanceRateLimitBucket.None;
    }

    /// <summary>COIN-M WebSocket API weight is shared with REST, and REST is shared with USD-M.</summary>
    private static BinanceRateLimitBucket ToBucketFuturesCoinM(bool isWeight, bool isOrders, long windowMillis)
    {
        if (isWeight && windowMillis == SysTime.MillisPerMinute()) return BinanceRateLimitBucket.FuturesRequestWeight;
        if (isOrders && windowMillis == SysTime.MillisPerTenSeconds()) return BinanceRateLimitBucket.FuturesOrderTenSeconds;
        if (isOrders && windowMillis == SysTime.MillisPerMinute()) return BinanceRateLimitBucket.FuturesOrderMinute;

        return BinanceRateLimitBucket.None;
    }

    private static BinanceRateLimitBucket ToBucketSpot(bool isWeight, bool isOrders, long windowMillis)
    {
        if (isWeight && windowMillis == SysTime.MillisPerMinute()) return BinanceRateLimitBucket.SpotWsApiRequestWeight;
        if (isOrders && windowMillis == SysTime.MillisPerTenSeconds()) return BinanceRateLimitBucket.SpotOrderTenSeconds;
        if (isOrders && windowMillis == SysTime.MillisPerDay()) return BinanceRateLimitBucket.SpotOrderDay;

        return BinanceRateLimitBucket.None;
    }
}
