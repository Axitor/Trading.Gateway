using Axitor.Trading.Gateway.Binance.DialectHelpers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Dialects;

/// <summary>Everything the engine has to know about Binance REST API specifically.</summary>
internal readonly struct BinanceDialectRestApi : IExchangeDialectRestApi
{
    // Response headers
    private const string RetryAfterHeader = "Retry-After";
    private const string UsedWeightHeader = "X-MBX-USED-WEIGHT-1M";
    private const string OrderCountMinuteHeader = "X-MBX-ORDER-COUNT-1M";
    private const string OrderCountTenSecondsHeader = "X-MBX-ORDER-COUNT-10S";
    private const string OrderCountDayHeader = "X-MBX-ORDER-COUNT-1D";

    /// <summary>Parse response headers (can be absent) and updates rate limiter weights of the market.</summary>
    public static void ObserveHeaders<TLimiter>(TLimiter limiter, HttpResponseMessage response, int market, ref RestApiRequestResultHeaders headers) where TLimiter : IRateLimiter
    {
        headers.RetryAfterSeconds = RestApiHeaderReader.ReadInt(response, RetryAfterHeader);

        switch ((BinanceMarkets)market)
        {
            // USD-M and COIN-M share one weight budget and one order budget.
            case BinanceMarkets.FuturesUsdM:
            case BinanceMarkets.FuturesCoinM:
                ObserveHeadersFutures(limiter, response);
                return;

            case BinanceMarkets.Spot:
                ObserveHeadersSpot(limiter, response);
                return;
        }
    }

    private static void ObserveHeadersFutures<TLimiter>(TLimiter limiter, HttpResponseMessage response) where TLimiter : IRateLimiter
    {
        int usedRequestWeight = RestApiHeaderReader.ReadInt(response, UsedWeightHeader);
        int usedOrderCountMinute = RestApiHeaderReader.ReadInt(response, OrderCountMinuteHeader);
        int usedOrderCountTenSeconds = RestApiHeaderReader.ReadInt(response, OrderCountTenSecondsHeader);

        if (usedRequestWeight != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.FuturesRequestWeight, usedRequestWeight);
        if (usedOrderCountMinute != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.FuturesOrderMinute, usedOrderCountMinute);
        if (usedOrderCountTenSeconds != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.FuturesOrderTenSeconds, usedOrderCountTenSeconds);
    }

    private static void ObserveHeadersSpot<TLimiter>(TLimiter limiter, HttpResponseMessage response) where TLimiter : IRateLimiter
    {
        int usedRequestWeight = RestApiHeaderReader.ReadInt(response, UsedWeightHeader);
        int usedOrderCountTenSeconds = RestApiHeaderReader.ReadInt(response, OrderCountTenSecondsHeader);
        int usedOrderCountDay = RestApiHeaderReader.ReadInt(response, OrderCountDayHeader);

        if (usedRequestWeight != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.SpotRequestWeight, usedRequestWeight);
        if (usedOrderCountTenSeconds != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.SpotOrderTenSeconds, usedOrderCountTenSeconds);
        if (usedOrderCountDay != 0) limiter.UpdateWeights((int)BinanceRateLimitBucket.SpotOrderDay, usedOrderCountDay);
    }

    /// <summary>
    /// Cold path.
    /// Reads the exchange error from the body and classifies the failure by status and code.
    /// Allocates strings on the managed heap only for what is captured.
    /// </summary>
    public static void ReadRestApiError(
        ReadOnlySpan<byte> body,
        int statusCode,
        int market,
        bool captureMessage,
        bool captureData,
        out RestApiRequestErrorReason reason,
        out int exchangeCode,
        out string? exchangeMessage,
        out string? exchangeData)
    {
        BinanceErrorReader.ParseError(body, captureMessage, captureData, out exchangeCode, out exchangeMessage, out exchangeData, out bool isDuplicateOrderMessage);
        BinanceErrorKind kind = BinanceErrorClassifier.Classify(statusCode, exchangeCode, isDuplicateOrderMessage, (BinanceMarkets)market);
        reason = ToReason(kind);
    }

    /// <summary>
    /// Cold path.
    /// Classifies an exchange error code to resiliency action
    /// </summary>
    public static RequestErrorAction ClassifyExchangeCodeAction(int exchangeCode, int market, bool isRetryable) =>
        BinanceErrorClassifier.ClassifyExchangeCodeAction(exchangeCode, (BinanceMarkets)market, isRetryable);

    private static RestApiRequestErrorReason ToReason(BinanceErrorKind kind) =>
        kind switch
        {
            BinanceErrorKind.Unauthorized => RestApiRequestErrorReason.ExchangeApiUnauthorized,
            BinanceErrorKind.BanLong => RestApiRequestErrorReason.ExchangeApiBanLong,
            BinanceErrorKind.BanShort => RestApiRequestErrorReason.ExchangeApiBanShort,
            BinanceErrorKind.NotFound => RestApiRequestErrorReason.StatusCodeNotFound,
            BinanceErrorKind.Malformed => RestApiRequestErrorReason.ExchangeApiRequestMalformed,
            BinanceErrorKind.Partial => RestApiRequestErrorReason.ExchangeApiRequestPartial,
            BinanceErrorKind.AlreadyPlaced => RestApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced,
            BinanceErrorKind.TimestampRejected => RestApiRequestErrorReason.ExchangeApiTimestampRejected,
            BinanceErrorKind.StatusUnknown => RestApiRequestErrorReason.ExchangeApiRequestStatusUnknown,
            BinanceErrorKind.RequestFailed => RestApiRequestErrorReason.ExchangeApiRequestFailed,
            BinanceErrorKind.RequestError => RestApiRequestErrorReason.ExchangeApiRequestError,
            _ => RestApiRequestErrorReason.StatusCodeOtherError,
        };
}
