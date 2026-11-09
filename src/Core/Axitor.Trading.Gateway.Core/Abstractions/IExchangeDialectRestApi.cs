using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Everything about reading a response that differs between exchanges REST API.</summary>
internal interface IExchangeDialectRestApi
{
    /// <summary>Parse response headers (can be absent) and updates rate limiter weights of the market. Market is opaque to the core.</summary>
    static abstract void ObserveHeaders<TLimiter>(TLimiter limiter, HttpResponseMessage response, int market, ref RestApiRequestResultHeaders headers) where TLimiter : IRateLimiter;

    /// <summary>Evaluates the HTTP status code and extracts exchange error data from the response stream. Market is opaque to the core.</summary>
    static abstract void ReadRestApiError(ReadOnlySpan<byte> body, int statusCode, int market, bool captureMessage, bool captureData, out RestApiRequestErrorReason reason, out int code, out string? message, out string? data);

    /// <summary>Classifies an exchange error code to resiliency action. Market is opaque to the core.</summary>
    static abstract RequestErrorAction ClassifyExchangeCodeAction(int exchangeCode, int market, bool isRetryable);

}
