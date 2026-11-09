using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>
/// USD-M futures trading over the WebSocket API. Signed: the client needs an API key and secret.
/// Orders go to their own critical connection by default, so routine calls never hold them back.
/// Orders need the filters of their symbol: load them once on start, see UsdMRestTradeSamples.LoadFiltersAsync.
/// </summary>
internal static class UsdMWsApiTradeSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>
    /// Places a LIMIT order. Price and quantity are rounded to the filters before sending.
    /// No retries by default: a resend after a lost answer could place a second order.
    /// </summary>
    public static async Task NewOrderAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters)
    {
        NewOrderRequest request = new()
        {
            Symbol = _symbol,

            // Your own id of the order, up to 36 characters. Use a new one for every order.
            ClientOrderId = ClientOrderId.Parse("my-order-1"),

            Filters = filters,
            Side = OrderSide.BUY,
            Type = OrderType.LIMIT,
            TimeInForce = TimeInForce.GTC,
            Quantity = 0.01,
            Price = 60000,
        };

        WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(in request).ConfigureAwait(false);

        if (result.Outcome == RequestOutcome.Success)
        {
            Console.WriteLine($"Order {result.Value.OrderId} placed: {result.Value.Status}");
            return;
        }

        switch (result.Errors.Reason)
        {
            case WsApiRequestErrorReason.OrderFiltersViolated:
                // Not sent: the order breaks a rule of the symbol.
                Console.WriteLine($"Not sent: {(OrderRuleViolation)result.Errors.RuleViolation}");
                break;

            case WsApiRequestErrorReason.NotSent:
            case WsApiRequestErrorReason.NoConnection:
            case WsApiRequestErrorReason.InFlightLimit:
                // Never reached the exchange: safe to send again.
                Console.WriteLine($"Not sent: {result.Errors.Reason}");
                break;

            case WsApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced:
                // An order with this client order id is already open.
                Console.WriteLine("Already placed");
                break;

            default:
                // A failure after sending, e.g. a timeout or a lost connection, may still mean the order was placed.
                // Check open orders by your client order id before you send it again.
                Console.WriteLine($"Order failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
                break;
        }
    }

    /// <summary>Cancels an order by the id the exchange gave it.</summary>
    public static async Task CancelOrderAsync(BinanceClient<NoOpTelemetryHandler> client, long orderId)
    {
        WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.CancelOrder.CancelAsync(_symbol, orderId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.OrderId}: {result.Value.Status}" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an order by your client order id.</summary>
    public static async Task CancelOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, ClientOrderId clientOrderId)
    {
        WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.CancelOrder.CancelAsync(_symbol, in clientOrderId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.ClientOrderId}: {result.Value.Status}" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Changes the price and quantity of an open LIMIT order, found by the exchange id. The order goes to the back of its price queue.</summary>
    public static async Task ModifyOrderAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters, long orderId)
    {
        // The side must match the order. isReduceOnly is only a check here; it does not change the order.
        WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.ModifyOrder
            .ModifyAsync(_symbol, orderId, OrderSide.BUY, quantity: 0.02, price: 59000, isReduceOnly: false, in filters)
            .ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.OrderId}: {result.Value.Price} x {result.Value.Quantity}" : $"Modify failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Changes the price and quantity of an open LIMIT order, found by your client order id.</summary>
    public static async Task ModifyOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters, ClientOrderId clientOrderId)
    {
        WsApiRequestResult<OrderModel> result = await client.Futures.UsdM.WsApi.Trade.ModifyOrder
            .ModifyAsync(_symbol, in clientOrderId, OrderSide.BUY, quantity: 0.02, price: 59000, isReduceOnly: false, in filters)
            .ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.ClientOrderId}: {result.Value.Price} x {result.Value.Quantity}" : $"Modify failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Places a stop loss that closes the whole long position. It waits on the exchange and goes to the order book once triggered.</summary>
    public static async Task NewAlgoOrderAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters)
    {
        NewAlgoOrderRequest request = new()
        {
            Symbol = _symbol,

            // Your own id of the algo order, up to 36 characters. Use a new one for every order.
            ClientAlgoId = ClientOrderId.Parse("my-stop-1"),

            Filters = filters,
            Side = OrderSide.SELL,
            Type = OrderType.STOP_MARKET,
            TriggerPrice = 55000,

            // Trigger by mark price, not last price.
            WorkingType = WorkingType.MARK_PRICE,

            // Close the whole position when triggered. No quantity needed.
            IsClosePosition = true,
        };

        WsApiRequestResult<AlgoOrderModel> result = await client.Futures.UsdM.WsApi.Trade.NewAlgoOrder.PlaceAsync(in request).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.AlgoId}: {result.Value.Status}" : $"Algo order failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an algo order by the id the exchange gave it.</summary>
    public static async Task CancelAlgoOrderAsync(BinanceClient<NoOpTelemetryHandler> client, long algoId)
    {
        WsApiRequestResult<AlgoOrderCancelModel> result = await client.Futures.UsdM.WsApi.Trade.CancelAlgoOrder.CancelAsync(algoId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.AlgoId} canceled" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an algo order by your client algo id.</summary>
    public static async Task CancelAlgoOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, ClientOrderId clientAlgoId)
    {
        WsApiRequestResult<AlgoOrderCancelModel> result = await client.Futures.UsdM.WsApi.Trade.CancelAlgoOrder.CancelAsync(in clientAlgoId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.ClientAlgoId} canceled" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }
}
