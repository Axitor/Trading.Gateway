namespace Axitor.Trading.Gateway.Binance.OrderRules;

/// <summary>Which symbol rule an order breaks.</summary>
public enum OrderRuleViolation : byte
{
    /// <summary>Every rule passes.</summary>
    None,

    /// <summary>Price below MinPrice.</summary>
    PriceBelowMin,

    /// <summary>Price above MaxPrice.</summary>
    PriceAboveMax,

    /// <summary>Quantity below the minimum.</summary>
    QuantityBelowMin,

    /// <summary>Quantity above the maximum.</summary>
    QuantityAboveMax,

    /// <summary>Price times quantity below MinNotional.</summary>
    NotionalBelowMin,
}
