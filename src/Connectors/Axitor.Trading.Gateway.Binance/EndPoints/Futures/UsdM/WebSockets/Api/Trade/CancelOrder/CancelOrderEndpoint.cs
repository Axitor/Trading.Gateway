using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.CancelOrder;

/// <summary>Cancel Order. Signed endpoint.</summary>
public sealed class CancelOrderEndpoint<TTelemetry> : BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal CancelOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, CancelOrderPayload.Method) { }

    /// <summary>
    /// Cancels an open order by the id the exchange gave it.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="orderId">Order id the exchange gave the order.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='WsApiFatalException']/node()"/></exception>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='InvalidOperationException']/node()"/></exception>
    public ValueTask<WsApiRequestResult<OrderModel>> CancelAsync(
        Symbol symbol,
        long orderId,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, orderId, ClientOrderId.Empty, isRetryable, timeToLiveMs, priority, cancellationToken);

    /// <summary>
    /// Cancels an open order by your client order id.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="clientOrderId">Client order id the order was placed with.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='WsApiFatalException']/node()"/></exception>
    /// <exception><inheritdoc cref="BinanceDocParams.WsApiDefinitions" path="/exception[@name='InvalidOperationException']/node()"/></exception>
    public ValueTask<WsApiRequestResult<OrderModel>> CancelAsync(
        Symbol symbol,
        in ClientOrderId clientOrderId,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, 0, in clientOrderId, isRetryable, timeToLiveMs, priority, cancellationToken);

    private ValueTask<WsApiRequestResult<OrderModel>> SendAsync(
        Symbol symbol,
        long orderId,
        in ClientOrderId clientOrderId,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        CancellationToken cancellationToken)
    {
        WsApiEndpointDescriptor descriptor = new()
        {
            Method = _method,
            Weight = 1,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesWsApiRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
        };

        CancelOrderPayload payload = new()
        {
            Symbol = symbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
        };

        return _router.SendWsApiSignedAsync<BinanceWsApiEventDispatcher, CancelOrderPayload, OrderParser, OrderModel>(
            _router.FuturesUsdMWsApi, descriptor, payload, cancellationToken);
    }
}
