using Axitor.Trading.Gateway.Binance.Models.Orders;

namespace Axitor.Trading.Gateway.Binance.OrderRules;

/// <summary>Rounds an order to the symbol filters and checks it before it is sent, ahead of the rate limiter.</summary>
/// <remarks>
/// DESIGN NOTE: A rule broken here would be rejected by the exchange anyway; checking it first saves the order rate limit and a round trip.
/// The min notional is checked only when the price is known and the order is neither reduce-only nor close-position: the exchange skips it there too.
/// </remarks>
internal static class BinanceOrderPreflight
{
    /// <summary>Rounds and checks a new order. None when it may be sent.</summary>
    public static OrderRuleViolation Prepare(ref NewOrderRequest request)
    {
        bool isMarket = BinanceOrderRules.IsMarketType(request.Type);

        OrderRuleViolation violation = PrepareQuantity(in request.Filters, ref request.Quantity, isMarket);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        violation = PreparePrice(in request.Filters, ref request.Price);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        return 0 < request.Price && !request.IsReduceOnly
            ? BinanceOrderRules.CheckNotional(in request.Filters, request.Price, request.Quantity)
            : OrderRuleViolation.None;
    }

    /// <summary>Rounds and checks a new algo order. None when it may be sent.</summary>
    public static OrderRuleViolation Prepare(ref NewAlgoOrderRequest request)
    {
        bool isMarket = BinanceOrderRules.IsMarketType(request.Type);
        OrderRuleViolation violation;

        // A close-position order sends no quantity.
        if (!request.IsClosePosition)
        {
            violation = PrepareQuantity(in request.Filters, ref request.Quantity, isMarket);

            if (violation != OrderRuleViolation.None)
            {
                return violation;
            }
        }

        violation = PreparePrice(in request.Filters, ref request.Price);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        violation = PreparePrice(in request.Filters, ref request.TriggerPrice);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        violation = PreparePrice(in request.Filters, ref request.ActivatePrice);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        return 0 < request.Price && !request.IsReduceOnly && !request.IsClosePosition
            ? BinanceOrderRules.CheckNotional(in request.Filters, request.Price, request.Quantity)
            : OrderRuleViolation.None;
    }

    /// <summary>Rounds and checks the new quantity and price of a modified LIMIT order. None when it may be sent.</summary>
    public static OrderRuleViolation PrepareModify(in SymbolFilters filters, ref double quantity, ref double price, bool isReduceOnly)
    {
        OrderRuleViolation violation = PrepareQuantity(in filters, ref quantity, isMarket: false);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        violation = PreparePrice(in filters, ref price);

        if (violation != OrderRuleViolation.None)
        {
            return violation;
        }

        return 0 < price && !isReduceOnly
            ? BinanceOrderRules.CheckNotional(in filters, price, quantity)
            : OrderRuleViolation.None;
    }

    private static OrderRuleViolation PrepareQuantity(in SymbolFilters filters, ref double quantity, bool isMarket)
    {
        quantity = BinanceOrderRules.RoundQuantity(in filters, quantity, isMarket);
        return BinanceOrderRules.CheckQuantity(in filters, quantity, isMarket);
    }

    /// <summary>Rounds and checks a price. Zero means the price is not sent and is left as is.</summary>
    private static OrderRuleViolation PreparePrice(in SymbolFilters filters, ref double price)
    {
        if (price <= 0)
        {
            return OrderRuleViolation.None;
        }

        price = BinanceOrderRules.RoundPrice(in filters, price);
        return BinanceOrderRules.CheckPrice(in filters, price);
    }
}
