using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Binance.DialectHelpers;

/// <summary>
/// Maps Binance statuses and error codes to failure kinds for REST and WebSocket API on every market.
/// Where the market docs disagree, the pessimistic reading wins: 408 and any 5xx mean the execution status is unknown,
/// and only codes the docs call a definite failure narrow it down.
/// Codes are split by market: the same number can mean different things.
/// </summary>
internal static class BinanceErrorClassifier
{
    // Spot -2010 NEW_ORDER_REJECTED message when the client order id is already in use.
    private static ReadOnlySpan<byte> DuplicateOrderMessage => "Duplicate order sent."u8;

    /// <summary>Message bytes that mark a spot duplicate order.</summary>
    public static ReadOnlySpan<byte> SpotDuplicateOrderMessage => DuplicateOrderMessage;

    /// <summary>Cold path. Classifies a failed response. The first matching rule wins.</summary>
    public static BinanceErrorKind Classify(int statusCode, int exchangeCode, bool isDuplicateOrderMessage, BinanceMarkets market)
    {
        // The status alone decides.
        switch (statusCode)
        {
            // Unauthorized
            case 401:
                return BinanceErrorKind.Unauthorized;

            // Not found
            case 404:
                return BinanceErrorKind.NotFound;

            // The exchange said to back off by Retry-After.
            case 429:
                return BinanceErrorKind.BanShort;

            // 403 WAF rule violated (rate limit or security block), 418 IP auto-banned. Every request extends the ban.
            case 403 or 418:
                return BinanceErrorKind.BanLong;

            // The request is malformed. The server rejected it.
            case 413 or 414 or 415:
                return BinanceErrorKind.Malformed;

            // Spot cancelReplace partly succeeded: either the cancel or the new order went through, not both. The error data says which.
            case 409:
                return BinanceErrorKind.Partial;
        }

        if (IsDuplicateOrder(exchangeCode, isDuplicateOrderMessage, market))
        {
            return BinanceErrorKind.AlreadyPlaced;
        }

        // A fatal code wins over any status, e.g. -1016 on a 503.
        if (IsFatalCode(exchangeCode, market))
        {
            return BinanceErrorKind.RequestError;
        }

        if (IsTimestampRejected(exchangeCode, market))
        {
            return BinanceErrorKind.TimestampRejected;
        }

        switch (exchangeCode)
        {
            // -1000 UNKNOWN: An unknown error occurred while processing the request.
            // -1006 UNEXPECTED_RESP: An unexpected response was received from the message bus. Execution status unknown.
            // -1007 TIMEOUT: Timeout waiting for response from backend server. Send status unknown; execution status unknown.
            case -1000 or -1006 or -1007:
                return BinanceErrorKind.StatusUnknown;

            // -1001 DISCONNECTED: Internal error; unable to process your request. Please try again.
            // -1008 SERVER_BUSY (spot) / Request Throttled (futures): Server is currently overloaded with other requests. Please try again in a few minutes.
            case -1001 or -1008:
                return BinanceErrorKind.RequestFailed;
        }

        // Spot: any 5xx is an unknown execution status. Futures: 408 is a backend timeout and a 503 can go either way.
        if (statusCode == 408 || (500 <= statusCode && statusCode <= 599))
        {
            return BinanceErrorKind.StatusUnknown;
        }

        // Remaining 4xx carry an exchange code and message.
        if (400 <= statusCode && statusCode <= 499)
        {
            return BinanceErrorKind.RequestError;
        }

        // Any other status code except 200 ok.
        return BinanceErrorKind.OtherError;
    }

    /// <summary>Cold path. Resiliency action for a RequestError, by its exchange code.</summary>
    public static RequestErrorAction ClassifyExchangeCodeAction(int exchangeCode, BinanceMarkets market, bool isRetryable)
    {
        if (IsFatalCode(exchangeCode, market))
        {
            return RequestErrorAction.Fatal;
        }

        // -1034 TOO_MANY_CONNECTIONS (spot): Too many concurrent connections; current limit is '%s'.
        if (market == BinanceMarkets.Spot && exchangeCode == -1034)
        {
            return isRetryable ? RequestErrorAction.Retry : RequestErrorAction.Skip;
        }

        // Can not be retryable without an user/bot/strategy decision.
        return RequestErrorAction.Skip;
    }

    private static bool IsFatalCode(int exchangeCode, BinanceMarkets market)
    {
        switch (exchangeCode)
        {
            // Every market:
            // -1002 UNAUTHORIZED: You are not authorized to execute this request.
            // -1016 SERVICE_SHUTTING_DOWN: This service is no longer available.
            // -1022 INVALID_SIGNATURE: Signature for this request is not valid.
            // -2014 BAD_API_KEY_FMT: API-key format invalid.
            // -2015 REJECTED_MBX_KEY: Invalid API-key, IP, or permissions for action.
            case -1002 or -1016 or -1022 or -2014 or -2015:
                return true;
        }

        return market switch
        {
            BinanceMarkets.FuturesUsdM => IsFatalCodeFuturesUsdM(exchangeCode),
            BinanceMarkets.FuturesCoinM => IsFatalCodeFuturesCoinM(exchangeCode),
            _ => false,
        };
    }

    private static bool IsFatalCodeFuturesUsdM(int exchangeCode)
    {
        switch (exchangeCode)
        {
            // -1099 NOT_FOUND: Not found, unauthenticated, or unauthorized.
            // -2017 API_KEYS_LOCKED: API Keys are locked on this account.
            // -2023 USER_IN_LIQUIDATION: User in liquidation mode now.
            // -4056 INVALID_API_KEY_TYPE: Invalid api key type.
            // -4057 INVALID_RSA_PUBLIC_KEY: Invalid api public key.
            case -1099 or -2017 or -2023 or -4056 or -4057:
                return true;

            default:
                return false;
        }
    }

    private static bool IsFatalCodeFuturesCoinM(int exchangeCode)
    {
        switch (exchangeCode)
        {
            // -2023 USER_IN_LIQUIDATION: User in liquidation mode now.
            // -4056 INVALID_API_KEY_TYPE: Invalid api key type.
            // -4057 INVALID_RSA_PUBLIC_KEY: Invalid api public key.
            case -2023 or -4056 or -4057:
                return true;

            default:
                return false;
        }
    }

    private static bool IsDuplicateOrder(int exchangeCode, bool isDuplicateOrderMessage, BinanceMarkets market) =>
        market switch
        {
            // -4116 DUPLICATED_CLIENT_ORDER_ID: clientOrderId is duplicated.
            BinanceMarkets.FuturesUsdM => exchangeCode == -4116,

            // -2010 NEW_ORDER_REJECTED with "Duplicate order sent.": the clOrdId is already in use.
            BinanceMarkets.Spot => exchangeCode == -2010 && isDuplicateOrderMessage,

            // COIN-M documents no duplicate client order id code.
            _ => false,
        };

    private static bool IsTimestampRejected(int exchangeCode, BinanceMarkets market)
    {
        // -1021 INVALID_TIMESTAMP: Timestamp for this request is outside of the recvWindow. / Timestamp for this request was 1000ms ahead of the server's time.
        if (exchangeCode == -1021)
        {
            return true;
        }

        return market switch
        {
            // -5028 ME_RECVWINDOW_REJECT: Timestamp for this request is outside of the ME recvWindow.
            BinanceMarkets.FuturesUsdM => exchangeCode == -5028,

            // -4188 ME_INVALID_TIMESTAMP: Timestamp for this request is outside of the ME recvWindow.
            BinanceMarkets.FuturesCoinM => exchangeCode == -4188,

            _ => false,
        };
    }
}
