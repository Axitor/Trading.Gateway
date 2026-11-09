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

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewAlgoOrder;

/// <summary>New Algo Order. Signed endpoint.</summary>
public sealed class NewAlgoOrderEndpoint<TTelemetry> : BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal NewAlgoOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, NewAlgoOrderPayload.Method) { }

    /// <summary>
    /// Places a conditional algo order: stop loss, take profit or trailing stop. It waits in the Algo Service and goes to the matching engine once triggered.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Security: Signed endpoint.
    /// Weight: 1 on the 10s and 1m order limits, 0 on the IP limit.
    /// </summary>
    /// <param name="request">The algo order. Read the ClientAlgoId and Filters field docs.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='WsApiFatalException']/node()"/></exception>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='InvalidOperationException']/node()"/></exception>
    public ValueTask<WsApiRequestResult<AlgoOrderModel>> PlaceAsync(
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
            return _router.RejectWsApi<BinanceWsApiEventDispatcher, AlgoOrderModel>(_router.FuturesUsdMWsApi, _method, violation);
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

        NewAlgoOrderPayload payload = new()
        {
            Request = prepared,
        };

        return _router.SendWsApiSignedAsync<BinanceWsApiEventDispatcher, NewAlgoOrderPayload, AlgoOrderParser, AlgoOrderModel>(
            _router.FuturesUsdMWsApi, descriptor, payload, cancellationToken);
    }
}
