using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Models.Responses;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>
/// USD-M futures REST trading. Signed endpoints: the client needs an API key and secret.
/// Orders need the filters of their symbol: load them once on start with LoadFiltersAsync and keep them.
/// </summary>
internal static class UsdMRestTradeSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Filters of one symbol from ExchangeInfo. Load them on start and again when the rules change.</summary>
    public static async Task<SymbolFilters> LoadFiltersAsync(BinanceClient<NoOpTelemetryHandler> client, Symbol symbol)
    {
        RestApiRequestResult<ExchangeInfoModel> result = await client.Futures.UsdM.RestApi.MarketData.ExchangeInfo.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            throw new InvalidOperationException($"Exchange info failed: {result.Errors.Reason}");
        }

        foreach (ExchangeSymbol item in result.Value.Symbols)
        {
            if (item.Symbol == symbol)
            {
                return item.Filters;
            }
        }

        throw new InvalidOperationException($"{symbol} is not listed.");
    }

    /// <summary>Rounds and checks a price and a quantity by the filters, as order endpoints do before sending.</summary>
    public static void OrderRules(SymbolFilters filters)
    {
        // Price goes to the nearest tick, quantity down to the step.
        double price = BinanceOrderRules.RoundPrice(in filters, 60000.123);
        double quantity = BinanceOrderRules.RoundQuantity(in filters, 0.0123, isMarket: false);

        OrderRuleViolation priceCheck = BinanceOrderRules.CheckPrice(in filters, price);
        OrderRuleViolation quantityCheck = BinanceOrderRules.CheckQuantity(in filters, quantity, isMarket: false);
        OrderRuleViolation notionalCheck = BinanceOrderRules.CheckNotional(in filters, price, quantity);

        Console.WriteLine($"Price {price}: {priceCheck}, quantity {quantity}: {quantityCheck}, notional: {notionalCheck}");
    }

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

        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(in request).ConfigureAwait(false);

        if (result.Outcome == RequestOutcome.Success)
        {
            Console.WriteLine($"Order {result.Value.OrderId} placed: {result.Value.Status}");
            return;
        }

        switch (result.Errors.Reason)
        {
            case RestApiRequestErrorReason.OrderFiltersViolated:
                // Not sent: the order breaks a rule of the symbol.
                Console.WriteLine($"Not sent: {(OrderRuleViolation)result.Errors.RuleViolation}");
                break;

            case RestApiRequestErrorReason.ExchangeApiOrderAlreadyPlaced:
                // An order with this client order id is already open.
                Console.WriteLine("Already placed");
                break;

            default:
                // A failure after sending may still mean the order was placed.
                // Check open orders by your client order id before you send it again.
                Console.WriteLine($"Order failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
                break;
        }
    }

    /// <summary>Checks an order like NewOrder would send it, but does not place it. Success carries no order data.</summary>
    public static async Task TestOrderAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters)
    {
        NewOrderRequest request = new()
        {
            Symbol = _symbol,
            ClientOrderId = ClientOrderId.Parse("my-test-order-1"),
            Filters = filters,
            Side = OrderSide.BUY,
            Type = OrderType.LIMIT,
            TimeInForce = TimeInForce.GTC,
            Quantity = 0.01,
            Price = 60000,
        };

        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.TestOrder.TestAsync(in request).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? "Test order passed" : $"Test order failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an order by the id the exchange gave it.</summary>
    public static async Task CancelOrderAsync(BinanceClient<NoOpTelemetryHandler> client, long orderId)
    {
        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.CancelOrder.CancelAsync(_symbol, orderId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.OrderId}: {result.Value.Status}" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an order by your client order id.</summary>
    public static async Task CancelOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, ClientOrderId clientOrderId)
    {
        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.CancelOrder.CancelAsync(_symbol, in clientOrderId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.ClientOrderId}: {result.Value.Status}" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Changes the price and quantity of an open LIMIT order, found by the exchange id. The order goes to the back of its price queue.</summary>
    public static async Task ModifyOrderAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters, long orderId)
    {
        // The side must match the order. isReduceOnly is only a check here; it does not change the order.
        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.ModifyOrder
            .ModifyAsync(_symbol, orderId, OrderSide.BUY, quantity: 0.02, price: 59000, isReduceOnly: false, in filters)
            .ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.OrderId}: {result.Value.Price} x {result.Value.Quantity}" : $"Modify failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Changes the price and quantity of an open LIMIT order, found by your client order id.</summary>
    public static async Task ModifyOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, SymbolFilters filters, ClientOrderId clientOrderId)
    {
        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.ModifyOrder
            .ModifyAsync(_symbol, in clientOrderId, OrderSide.BUY, quantity: 0.02, price: 59000, isReduceOnly: false, in filters)
            .ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Order {result.Value.ClientOrderId}: {result.Value.Price} x {result.Value.Quantity}" : $"Modify failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>
    /// Places a stop loss that closes the whole long position. Stops, take profits and trailing stops are algo orders:
    /// they wait on the exchange and go to the order book once triggered.
    /// </summary>
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

        RestApiRequestResult<AlgoOrderModel> result = await client.Futures.UsdM.RestApi.Trade.NewAlgoOrder.PlaceAsync(in request).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.AlgoId}: {result.Value.Status}" : $"Algo order failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an algo order by the id the exchange gave it.</summary>
    public static async Task CancelAlgoOrderAsync(BinanceClient<NoOpTelemetryHandler> client, long algoId)
    {
        RestApiRequestResult<AlgoOrderCancelModel> result = await client.Futures.UsdM.RestApi.Trade.CancelAlgoOrder.CancelAsync(algoId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.AlgoId} canceled" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels an algo order by your client algo id.</summary>
    public static async Task CancelAlgoOrderByClientIdAsync(BinanceClient<NoOpTelemetryHandler> client, ClientOrderId clientAlgoId)
    {
        RestApiRequestResult<AlgoOrderCancelModel> result = await client.Futures.UsdM.RestApi.Trade.CancelAlgoOrder.CancelAsync(in clientAlgoId).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"Algo order {result.Value.ClientAlgoId} canceled" : $"Cancel failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Open orders of one symbol. Algo orders are listed by OpenAlgoOrders.</summary>
    public static async Task OpenOrdersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<OpenOrdersModel> result = await client.Futures.UsdM.RestApi.Trade.OpenOrders.GetAsync(_symbol).ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Open orders failed: {result.Errors.Reason}");
            return;
        }

        foreach (OrderModel order in result.Value.Orders)
        {
            Console.WriteLine($"{order.ClientOrderId}: {order.Side} {order.Quantity} at {order.Price}, filled {order.FilledQuantity}, {order.Status}");
        }
    }

    /// <summary>Open orders of every symbol. Costs 40 weight: prefer OpenOrdersAsync per symbol.</summary>
    public static async Task OpenOrdersAllAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<OpenOrdersModel> result = await client.Futures.UsdM.RestApi.Trade.OpenOrders.GetAllAsync().ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"{result.Value.Orders.Length} open orders" : $"Open orders failed: {result.Errors.Reason}");
    }

    /// <summary>Open algo orders of one symbol: stops, take profits and trailing stops not triggered yet.</summary>
    public static async Task OpenAlgoOrdersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<OpenAlgoOrdersModel> result = await client.Futures.UsdM.RestApi.Trade.OpenAlgoOrders.GetAsync(_symbol).ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Open algo orders failed: {result.Errors.Reason}");
            return;
        }

        foreach (AlgoOrderModel order in result.Value.Orders)
        {
            Console.WriteLine($"{order.ClientAlgoId}: {order.Type} {order.Side}, trigger {order.TriggerPrice}, {order.Status}");
        }
    }

    /// <summary>Open algo orders of every symbol. Costs 40 weight: prefer OpenAlgoOrdersAsync per symbol.</summary>
    public static async Task OpenAlgoOrdersAllAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<OpenAlgoOrdersModel> result = await client.Futures.UsdM.RestApi.Trade.OpenAlgoOrders.GetAllAsync().ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"{result.Value.Orders.Length} open algo orders" : $"Open algo orders failed: {result.Errors.Reason}");
    }

    /// <summary>Cancels every open order of one symbol. A retry after a lost answer also cancels orders placed in between.</summary>
    public static async Task CancelAllOpenOrdersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.CancelAllOpenOrders.CancelAsync(_symbol).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? "All open orders canceled" : $"Cancel all failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Cancels every open algo order of one symbol.</summary>
    public static async Task CancelAllAlgoOpenOrdersAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.CancelAllAlgoOpenOrders.CancelAsync(_symbol).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? "All open algo orders canceled" : $"Cancel all failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Sets isolated margin for one symbol. Not allowed while it has open orders or a position.</summary>
    public static async Task ChangeMarginTypeAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<AcknowledgeModel> result = await client.Futures.UsdM.RestApi.Trade.ChangeMarginType.ChangeAsync(_symbol, MarginType.ISOLATED).ConfigureAwait(false);

        if (result.Outcome == RequestOutcome.Success)
        {
            Console.WriteLine("Margin type changed");
            return;
        }

        // -4046 NO_NEED_TO_CHANGE_MARGIN_TYPE: No need to change margin type.
        if (result.Errors.ExchangeCode == -4046)
        {
            Console.WriteLine("Already isolated");
            return;
        }

        Console.WriteLine($"Change margin type failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }

    /// <summary>Sets the leverage of one symbol. The highest allowed is in its first leverage bracket.</summary>
    public static async Task ChangeLeverageAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<ChangeLeverageModel> result = await client.Futures.UsdM.RestApi.Trade.ChangeLeverage.ChangeAsync(_symbol, 10).ConfigureAwait(false);

        Console.WriteLine(result.Outcome == RequestOutcome.Success ? $"{result.Value.Symbol}: leverage {result.Value.Leverage}, max notional {result.Value.MaxNotionalValue}" : $"Change leverage failed: {result.Errors.Reason}, code {result.Errors.ExchangeCode}");
    }
}
