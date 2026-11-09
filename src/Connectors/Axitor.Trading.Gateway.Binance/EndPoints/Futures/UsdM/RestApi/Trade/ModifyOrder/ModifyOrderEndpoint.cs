using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ModifyOrder;

/// <summary>Modify Order. Signed endpoint.</summary>
public sealed class ModifyOrderEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal ModifyOrderEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Changes the quantity and price of an open LIMIT order, found by the id the exchange gave it. The order goes to the back of its price queue.
    /// The exchange cancels a partly filled order when the new quantity is not above the filled one, and a GTX order the new price would fill at once.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Security: Signed endpoint.
    /// Weight: 1 on the 10s and 1m order limits, 0 on the IP limit.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="orderId">Order id the exchange gave the order.</param>
    /// <param name="side">Side of the order. Must match the order.</param>
    /// <param name="quantity">New quantity, rounded down to the step before sending.</param>
    /// <param name="price">New price, rounded to the nearest tick before sending.</param>
    /// <param name="isReduceOnly">
    /// Whether the order is reduce-only. True skips the min notional check here and on the exchange,
    /// and the exchange rejects the change with -5047 when the order is not reduce-only. It does not change the order.
    /// </param>
    /// <param name="filters"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='filters']/node()"/></param>
    /// <param name="modifyId">Your id for this change, returned as is in the response. Zero leaves it out.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OrderModel>> ModifyAsync(
        Symbol symbol,
        long orderId,
        OrderSide side,
        double quantity,
        double price,
        bool isReduceOnly,
        in SymbolFilters filters,
        long modifyId = 0,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, orderId, ClientOrderId.Empty, side, quantity, price, isReduceOnly, in filters, modifyId, isRetryable, timeToLiveMs, priority, cancellationToken);

    /// <summary>
    /// Changes the quantity and price of an open LIMIT order, found by your client order id. The order goes to the back of its price queue.
    /// The exchange cancels a partly filled order when the new quantity is not above the filled one, and a GTX order the new price would fill at once.
    /// Note: Only the symbol filters are checked; other params are applied without validation.
    /// Security: Signed endpoint.
    /// Weight: 1 on the 10s and 1m order limits, 0 on the IP limit.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="clientOrderId">Client order id the order was placed with.</param>
    /// <param name="side">Side of the order. Must match the order.</param>
    /// <param name="quantity">New quantity, rounded down to the step before sending.</param>
    /// <param name="price">New price, rounded to the nearest tick before sending.</param>
    /// <param name="isReduceOnly">
    /// Whether the order is reduce-only. True skips the min notional check here and on the exchange,
    /// and the exchange rejects the change with -5047 when the order is not reduce-only. It does not change the order.
    /// </param>
    /// <param name="filters"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='filters']/node()"/></param>
    /// <param name="modifyId">Your id for this change, returned as is in the response. Zero leaves it out.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OrderModel>> ModifyAsync(
        Symbol symbol,
        in ClientOrderId clientOrderId,
        OrderSide side,
        double quantity,
        double price,
        bool isReduceOnly,
        in SymbolFilters filters,
        long modifyId = 0,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Critical,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, 0, in clientOrderId, side, quantity, price, isReduceOnly, in filters, modifyId, isRetryable, timeToLiveMs, priority, cancellationToken);

    private ValueTask<RestApiRequestResult<OrderModel>> SendAsync(
        Symbol symbol,
        long orderId,
        in ClientOrderId clientOrderId,
        OrderSide side,
        double quantity,
        double price,
        bool isReduceOnly,
        in SymbolFilters filters,
        long modifyId,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        CancellationToken cancellationToken)
    {
        OrderRuleViolation violation = BinanceOrderPreflight.PrepareModify(in filters, ref quantity, ref price, isReduceOnly);

        if (violation != OrderRuleViolation.None)
        {
            return _router.RejectRestApi<OrderModel>(violation);
        }

        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = 1,
            HttpMethod = HttpMethod.Put,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesOrder,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        ModifyOrderPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
            OrderId = orderId,
            ClientOrderId = clientOrderId,
            Side = side,
            Quantity = quantity,
            Price = price,
            ModifyId = modifyId,
            IsReduceOnly = isReduceOnly,
        };

        return _router.SendRestApiSignedAsync<ModifyOrderPayload, OrderParser, OrderModel>(descriptor, payload, cancellationToken);
    }
}
