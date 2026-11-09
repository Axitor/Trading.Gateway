using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewAlgoOrder;

/// <summary>New Algo Order. Signed endpoint.</summary>
public sealed class NewAlgoOrderEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal NewAlgoOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Places a conditional algo order: stop loss, take profit or trailing stop. It waits in the Algo Service and goes to the matching engine once triggered.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Security: Signed endpoint.
    /// Weight: 1 on the 10s and 1m order limits, 0 on the IP limit.
    /// </summary>
    /// <param name="request">The algo order. Read the ClientAlgoId and Filters field docs.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<AlgoOrderModel>> PlaceAsync(
        in NewAlgoOrderRequest request,
        bool isRetryable = false,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default)
    {
        NewAlgoOrderRequest prepared = request;
        OrderRuleViolation violation = BinanceOrderPreflight.Prepare(ref prepared);

        if (violation != OrderRuleViolation.None)
        {
            return _router.RejectRestApi<AlgoOrderModel>(violation);
        }

        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Post,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesOrder,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        NewAlgoOrderPayload payload = new()
        {
            Host = _host,
            Request = prepared,
        };

        return _router.SendRestApiSignedAsync<NewAlgoOrderPayload, AlgoOrderParser, AlgoOrderModel>(descriptor, payload, cancellationToken);
    }
}
