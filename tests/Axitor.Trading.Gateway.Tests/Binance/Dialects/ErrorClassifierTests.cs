using System.Text;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Tests.Binance.Dialects;

/// <summary>How a failed answer turns into a reason and an action. The table is the business rule.</summary>
public sealed class ErrorClassifierTests
{
    [Theory]
    // -2015 REJECTED_MBX_KEY: Invalid API-key, IP, or permissions for action.
    [InlineData(401, """{"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiUnauthorized)]
    [InlineData(404, "", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.StatusCodeNotFound)]
    // -1003 TOO_MANY_REQUESTS: Too many requests; current limit is %s requests per minute.
    [InlineData(429, """{"code":-1003,"msg":"Too many requests."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiBanShort)]
    [InlineData(418, """{"code":-1003,"msg":"Way too many requests; IP banned."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiBanLong)]
    [InlineData(403, "<html>403 Forbidden</html>", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiBanLong)]
    [InlineData(413, "", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestMalformed)]
    // -2021 ORDER_CANCEL_REPLACE_PARTIALLY_FAILED: Order cancel-replace partially failed.
    [InlineData(409, """{"code":-2021,"msg":"Order cancel-replace partially failed."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiRequestPartial)]
    // -4116 DUPLICATED_CLIENT_ORDER_ID: clientOrderId is duplicated.
    [InlineData(400, """{"code":-4116,"msg":"clientOrderId is duplicated."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced)]
    [InlineData(400, """{"code":-4116,"msg":"clientOrderId is duplicated."}""", BinanceMarkets.FuturesCoinM, RestApiRequestErrorReason.ExchangeApiRequestError)]
    // -2010 NEW_ORDER_REJECTED: Duplicate order sent. Only this message means the id is in use.
    [InlineData(400, """{"code":-2010,"msg":"Duplicate order sent."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced)]
    [InlineData(400, """{"code":-2010,"msg":"Account has insufficient balance for requested action."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiRequestError)]
    // -1016 SERVICE_SHUTTING_DOWN: This service is no longer available. A fatal code wins over the 503.
    [InlineData(503, """{"code":-1016,"msg":"This service is no longer available."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestError)]
    // -1021 INVALID_TIMESTAMP: Timestamp for this request is outside of the recvWindow.
    [InlineData(400, """{"code":-1021,"msg":"Timestamp for this request is outside of the recvWindow."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiTimestampRejected)]
    // -5028 ME_RECVWINDOW_REJECT: Timestamp for this request is outside of the ME recvWindow. USD-M only.
    [InlineData(400, """{"code":-5028,"msg":"Timestamp for this request is outside of the ME recvWindow."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiTimestampRejected)]
    [InlineData(400, """{"code":-5028,"msg":"Timestamp for this request is outside of the ME recvWindow."}""", BinanceMarkets.FuturesCoinM, RestApiRequestErrorReason.ExchangeApiRequestError)]
    // -4188 ME_INVALID_TIMESTAMP: Timestamp for this request is outside of the ME recvWindow. COIN-M only.
    [InlineData(400, """{"code":-4188,"msg":"Timestamp for this request is outside of the ME recvWindow."}""", BinanceMarkets.FuturesCoinM, RestApiRequestErrorReason.ExchangeApiTimestampRejected)]
    [InlineData(400, """{"code":-4188,"msg":"Timestamp for this request is outside of the ME recvWindow."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestError)]
    // -1007 TIMEOUT: Timeout waiting for response from backend server. Send status unknown; execution status unknown.
    [InlineData(408, """{"code":-1007,"msg":"Timeout waiting for response from backend server."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestStatusUnknown)]
    // -1000 UNKNOWN: An unknown error occurred while processing the request.
    [InlineData(500, """{"code":-1000,"msg":"An unknown error occurred while processing the request."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestStatusUnknown)]
    // -1001 DISCONNECTED: Internal error; unable to process your request. Please try again. The code wins over the 503.
    [InlineData(503, """{"code":-1001,"msg":"Internal error; unable to process your request. Please try again."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestFailed)]
    // -1008 SERVER_BUSY: Server is currently overloaded with other requests. Please try again in a few minutes.
    [InlineData(503, """{"code":-1008,"msg":"Server is currently overloaded with other requests."}""", BinanceMarkets.Spot, RestApiRequestErrorReason.ExchangeApiRequestFailed)]
    [InlineData(502, "<html>502 Bad Gateway</html>", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestStatusUnknown)]
    // -1102 MANDATORY_PARAM_EMPTY_OR_MALFORMED: A mandatory parameter was not sent, was empty/null, or malformed.
    [InlineData(400, """{"code":-1102,"msg":"Mandatory parameter 'symbol' was not sent, was empty/null, or malformed."}""", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.ExchangeApiRequestError)]
    [InlineData(302, "", BinanceMarkets.FuturesUsdM, RestApiRequestErrorReason.StatusCodeOtherError)]
    public void ClassifiesFailedAnswer(int statusCode, string body, BinanceMarkets market, RestApiRequestErrorReason expected)
    {
        BinanceDialectRestApi.ReadRestApiError(Encoding.UTF8.GetBytes(body), statusCode, (int)market, captureMessage: false, captureData: false,
            out RestApiRequestErrorReason reason, out _, out _, out _);

        Assert.Equal(expected, reason);
    }

    [Fact]
    public void CapturesCodeMessageAndData()
    {
        byte[] body = """{"code":-2021,"msg":"Order cancel-replace partially failed.","data":{"cancelResult":"SUCCESS","newOrderResult":"FAILURE"}}"""u8.ToArray();

        BinanceDialectRestApi.ReadRestApiError(body, 409, (int)BinanceMarkets.Spot, captureMessage: true, captureData: true,
            out RestApiRequestErrorReason reason, out int code, out string? message, out string? data);

        Assert.Equal(RestApiRequestErrorReason.ExchangeApiRequestPartial, reason);
        Assert.Equal(-2021, code);
        Assert.Equal("Order cancel-replace partially failed.", message);
        Assert.Equal("""{"cancelResult":"SUCCESS","newOrderResult":"FAILURE"}""", data);
    }

    [Theory]
    // -2015 REJECTED_MBX_KEY: Invalid API-key, IP, or permissions for action.
    [InlineData(-2015, BinanceMarkets.Spot, false, RequestErrorAction.Fatal)]
    // -1022 INVALID_SIGNATURE: Signature for this request is not valid.
    [InlineData(-1022, BinanceMarkets.FuturesUsdM, true, RequestErrorAction.Fatal)]
    // -1002 UNAUTHORIZED: You are not authorized to execute this request.
    [InlineData(-1002, BinanceMarkets.FuturesCoinM, true, RequestErrorAction.Fatal)]
    // -1099 NOT_FOUND: Not found, unauthenticated, or unauthorized. Fatal on USD-M only.
    [InlineData(-1099, BinanceMarkets.FuturesUsdM, true, RequestErrorAction.Fatal)]
    [InlineData(-1099, BinanceMarkets.Spot, true, RequestErrorAction.Skip)]
    // -2017 API_KEYS_LOCKED: API Keys are locked on this account. Fatal on USD-M only.
    [InlineData(-2017, BinanceMarkets.FuturesUsdM, true, RequestErrorAction.Fatal)]
    [InlineData(-2017, BinanceMarkets.FuturesCoinM, true, RequestErrorAction.Skip)]
    // -2023 USER_IN_LIQUIDATION: User in liquidation mode now.
    [InlineData(-2023, BinanceMarkets.FuturesCoinM, true, RequestErrorAction.Fatal)]
    // -1034 TOO_MANY_CONNECTIONS: Too many concurrent connections. Retried on spot only, and only when asked to.
    [InlineData(-1034, BinanceMarkets.Spot, true, RequestErrorAction.Retry)]
    [InlineData(-1034, BinanceMarkets.Spot, false, RequestErrorAction.Skip)]
    [InlineData(-1034, BinanceMarkets.FuturesUsdM, true, RequestErrorAction.Skip)]
    // -2019 MARGIN_NOT_SUFFICIEN: Margin is insufficient. A business rejection is never retried.
    [InlineData(-2019, BinanceMarkets.FuturesUsdM, true, RequestErrorAction.Skip)]
    public void ChoosesActionByCode(int code, BinanceMarkets market, bool isRetryable, RequestErrorAction expected) =>
        Assert.Equal(expected, BinanceDialectRestApi.ClassifyExchangeCodeAction(code, (int)market, isRetryable));
}
