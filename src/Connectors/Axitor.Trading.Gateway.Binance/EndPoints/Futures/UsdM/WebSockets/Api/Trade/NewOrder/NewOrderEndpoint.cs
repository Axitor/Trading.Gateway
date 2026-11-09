using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewOrder;

/// <summary>New Order. Signed endpoint.</summary>
public sealed class NewOrderEndpoint<TTelemetry> : BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal NewOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, NewOrderPayload.Method) { }

    /// <summary>
    /// Places a LIMIT or MARKET order. Stop, take profit and trailing stop orders go through NewAlgoOrder.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Security: Signed endpoint.
    /// Weight: 1 on the 10s and 1m order limits, 0 on the IP limit.
    /// </summary>
    /// <param name="request">The order. Read the ClientOrderId and Filters field docs.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='WsApiFatalException']/node()"/></exception>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='InvalidOperationException']/node()"/></exception>
    public ValueTask<WsApiRequestResult<OrderModel>> PlaceAsync(
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
            return _router.RejectWsApi<BinanceWsApiEventDispatcher, OrderModel>(_router.FuturesUsdMWsApi, _method, violation);
        }

        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _method,
            Weight = 1,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesOrder,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
        };

        NewOrderPayload payload = new()
        {
            Request = prepared,
        };

        return _router.SendWsApiSignedAsync<BinanceWsApiEventDispatcher, NewOrderPayload, OrderParser, OrderModel>(
            _router.FuturesUsdMWsApi, descriptor, payload, cancellationToken);
    }
}
