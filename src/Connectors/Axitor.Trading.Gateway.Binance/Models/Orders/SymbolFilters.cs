namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>Trading rules of one symbol, as exchangeInfo gives them. Fill once per symbol; zero turns a rule off.</summary>
public struct SymbolFilters
{
    /// <summary>Price step, PRICE_FILTER tickSize.</summary>
    public double TickSize;

    /// <summary>Lowest price, PRICE_FILTER minPrice. Prices are counted in ticks from it.</summary>
    public double MinPrice;

    /// <summary>Highest price, PRICE_FILTER maxPrice.</summary>
    public double MaxPrice;

    /// <summary>Quantity step, LOT_SIZE stepSize.</summary>
    public double StepSize;

    /// <summary>Lowest quantity, LOT_SIZE minQty. Quantities are counted in steps from it.</summary>
    public double MinQuantity;

    /// <summary>Highest quantity, LOT_SIZE maxQty.</summary>
    public double MaxQuantity;

    /// <summary>Quantity step of market orders, MARKET_LOT_SIZE stepSize. Zero uses StepSize.</summary>
    public double MarketStepSize;

    /// <summary>Lowest quantity of market orders, MARKET_LOT_SIZE minQty. Zero uses MinQuantity.</summary>
    public double MarketMinQuantity;

    /// <summary>Highest quantity of market orders, MARKET_LOT_SIZE maxQty. Zero uses MaxQuantity.</summary>
    public double MarketMaxQuantity;

    /// <summary>Lowest price times quantity, MIN_NOTIONAL notional.</summary>
    public double MinNotional;
}
