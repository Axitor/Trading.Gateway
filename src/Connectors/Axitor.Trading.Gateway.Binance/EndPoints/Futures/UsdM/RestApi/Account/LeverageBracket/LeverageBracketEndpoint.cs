using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;

/// <summary>Notional and Leverage Brackets. Signed endpoint.</summary>
public sealed class LeverageBracketEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal LeverageBracketEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Notional brackets of a symbol for the account: the highest initial leverage and the maintenance margin ratio of each notional range.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1. Symbol.Empty asks for every symbol, as GetAllAsync does.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<LeverageBracketModel>> GetAsync(
        Symbol symbol,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, isRetryable, timeToLiveMs, priority, cancellationToken);

    /// <summary>
    /// Notional brackets of every symbol for the account. Symbols whose name does not fit Symbol are left out.
    /// Cold path: the answer is large and allocates.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<LeverageBracketModel>> GetAllAsync(
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default) =>
        SendAsync(Symbol.Empty, isRetryable, timeToLiveMs, priority, cancellationToken);

    private ValueTask<RestApiRequestResult<LeverageBracketModel>> SendAsync(
        Symbol symbol,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        CancellationToken cancellationToken)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        LeverageBracketPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
        };

        return _router.SendRestApiSignedAsync<LeverageBracketPayload, LeverageBracketParser, LeverageBracketModel>(descriptor, payload, cancellationToken);
    }
}
