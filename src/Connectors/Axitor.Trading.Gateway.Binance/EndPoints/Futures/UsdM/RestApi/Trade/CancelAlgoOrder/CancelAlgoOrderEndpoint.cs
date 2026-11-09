using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAlgoOrder;

/// <summary>Cancel Algo Order. Signed endpoint.</summary>
public sealed class CancelAlgoOrderEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal CancelAlgoOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Cancels an open algo order by the id the exchange gave it.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="algoId">Algo order id the exchange gave the order.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<AlgoOrderCancelModel>> CancelAsync(
        long algoId,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(algoId, ClientOrderId.Empty, isRetryable, timeToLiveMs, priority, cancellationToken);

    /// <summary>
    /// Cancels an open algo order by your client algo id.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="clientAlgoId">Client algo id the order was placed with.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<AlgoOrderCancelModel>> CancelAsync(
        in ClientOrderId clientAlgoId,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(0, in clientAlgoId, isRetryable, timeToLiveMs, priority, cancellationToken);

    private ValueTask<RestApiRequestResult<AlgoOrderCancelModel>> SendAsync(
        long algoId,
        in ClientOrderId clientAlgoId,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        CancellationToken cancellationToken)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Delete,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        CancelAlgoOrderPayload payload = new()
        {
            Host = _host,
            AlgoId = algoId,
            ClientAlgoId = clientAlgoId,
        };

        return _router.SendRestApiSignedAsync<CancelAlgoOrderPayload, AlgoOrderCancelParser, AlgoOrderCancelModel>(descriptor, payload, cancellationToken);
    }
}
