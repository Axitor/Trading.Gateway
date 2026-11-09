using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;

namespace Axitor.Trading.Gateway.Binance.OrderRules;

/// <summary>Rounds and checks prices and quantities by the symbol filters, the same way order endpoints do before sending.</summary>
/// <remarks>
/// DESIGN NOTE: A price goes to the nearest tick and a quantity down to its step, so an order is never larger than asked.
/// Comparisons allow a tiny relative error: a rounded double such as 0.30000000000000004 must not fail a limit of 0.3.
/// </remarks>
public static class BinanceOrderRules
{
    // Far below any tick-to-price ratio an exchange lists, far above double rounding noise.
    private const double RelativeTolerance = 1e-12;

    // A quotient like 2.9999999999999996 steps counts as three steps.
    private const double StepTolerance = 1e-9;

    /// <summary>Price on the nearest tick, counted from MinPrice. Unchanged when TickSize is zero.</summary>
    public static double RoundPrice(in SymbolFilters filters, double price)
    {
        double tick = filters.TickSize;

        if (tick <= 0)
        {
            return price;
        }

        double origin = filters.MinPrice;
        return origin + Math.Round((price - origin) / tick, MidpointRounding.AwayFromZero) * tick;
    }

    /// <summary>Quantity down to the step, counted from the minimum. Unchanged when the step is zero.</summary>
    public static double RoundQuantity(in SymbolFilters filters, double quantity, bool isMarket)
    {
        double step = isMarket && 0 < filters.MarketStepSize ? filters.MarketStepSize : filters.StepSize;

        if (step <= 0)
        {
            return quantity;
        }

        double origin = GetMinQuantity(in filters, isMarket);
        return origin + Math.Floor((quantity - origin) / step + StepTolerance) * step;
    }

    /// <summary>Checks a price against MinPrice and MaxPrice.</summary>
    public static OrderRuleViolation CheckPrice(in SymbolFilters filters, double price)
    {
        if (0 < filters.MinPrice && IsBelow(price, filters.MinPrice))
        {
            return OrderRuleViolation.PriceBelowMin;
        }

        if (0 < filters.MaxPrice && IsAbove(price, filters.MaxPrice))
        {
            return OrderRuleViolation.PriceAboveMax;
        }

        return OrderRuleViolation.None;
    }

    /// <summary>Checks a quantity against the minimum and maximum for its order kind.</summary>
    public static OrderRuleViolation CheckQuantity(in SymbolFilters filters, double quantity, bool isMarket)
    {
        double minQuantity = GetMinQuantity(in filters, isMarket);
        double maxQuantity = isMarket && 0 < filters.MarketMaxQuantity ? filters.MarketMaxQuantity : filters.MaxQuantity;

        if (0 < minQuantity && IsBelow(quantity, minQuantity))
        {
            return OrderRuleViolation.QuantityBelowMin;
        }

        if (0 < maxQuantity && IsAbove(quantity, maxQuantity))
        {
            return OrderRuleViolation.QuantityAboveMax;
        }

        return OrderRuleViolation.None;
    }

    /// <summary>Checks price times quantity against MinNotional. Reduce-only and close-position orders skip it on the exchange.</summary>
    public static OrderRuleViolation CheckNotional(in SymbolFilters filters, double price, double quantity)
    {
        if (0 < filters.MinNotional && IsBelow(price * quantity, filters.MinNotional))
        {
            return OrderRuleViolation.NotionalBelowMin;
        }

        return OrderRuleViolation.None;
    }

    /// <summary>Whether the order type fills at market and follows the MARKET_LOT_SIZE rules.</summary>
    public static bool IsMarketType(OrderType type) =>
        type is OrderType.MARKET or OrderType.STOP_MARKET or OrderType.TAKE_PROFIT_MARKET or OrderType.TRAILING_STOP_MARKET;

    private static double GetMinQuantity(in SymbolFilters filters, bool isMarket) =>
        isMarket && 0 < filters.MarketMinQuantity ? filters.MarketMinQuantity : filters.MinQuantity;

    private static bool IsBelow(double value, double limit) => value < limit - limit * RelativeTolerance;

    private static bool IsAbove(double value, double limit) => limit + limit * RelativeTolerance < value;
}
