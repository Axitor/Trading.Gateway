using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Responses;
using Axitor.Trading.Gateway.Binance.Parsing.Responses;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeMarginType;

/// <summary>Change Margin Type. Signed endpoint.</summary>
public sealed class ChangeMarginTypeEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal ChangeMarginTypeEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Sets cross or isolated margin for a symbol.
    /// The exchange refuses with -4046 NO_NEED_TO_CHANGE_MARGIN_TYPE when the symbol already has it,
    /// -4047 THERE_EXISTS_OPEN_ORDERS and -4048 THERE_EXISTS_QUANTITY while orders or a position are open.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="marginType">CROSS or ISOLATED.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<AcknowledgeModel>> ChangeAsync(
        Symbol symbol,
        MarginType marginType,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Post,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        ChangeMarginTypePayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
            MarginType = marginType,
        };

        return _router.SendRestApiSignedAsync<ChangeMarginTypePayload, AcknowledgeParser, AcknowledgeModel>(descriptor, payload, cancellationToken);
    }
}
