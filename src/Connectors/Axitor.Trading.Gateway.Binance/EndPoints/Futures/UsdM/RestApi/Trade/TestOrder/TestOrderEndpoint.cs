using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewOrder;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Models.Responses;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Parsing.Responses;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.TestOrder;

/// <summary>Test Order. Signed endpoint.</summary>
public sealed class TestOrderEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal TestOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Checks an order exactly as NewOrder would send it, without placing it in the matching engine. The answer is empty.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Note: The exchange does not check the mark price band here, so a passed test order can still be rejected by NewOrder.
    /// Security: Signed endpoint.
    /// Weight: not stated by the exchange, charged as 1.
    /// </summary>
    /// <param name="request">The order. Read the ClientOrderId and Filters field docs.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<AcknowledgeModel>> TestAsync(
        in NewOrderRequest request,
        bool isRetryable = false,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default)
    {
        NewOrderRequest prepared = request;
        OrderRuleViolation violation = BinanceOrderPreflight.Prepare(ref prepared);

        if (violation != OrderRuleViolation.None)
        {
            return _router.RejectRestApi<AcknowledgeModel>(violation);
        }

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

        NewOrderPayload payload = new()
        {
            Host = _host,
            Request = prepared,
            IsTest = true,
        };

        return _router.SendRestApiSignedAsync<NewOrderPayload, AcknowledgeParser, AcknowledgeModel>(descriptor, payload, cancellationToken);
    }
}
