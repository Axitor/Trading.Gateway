namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>One price level of an order book.</summary>
public struct DepthLevel
{
    /// <summary>Price.</summary>
    public double Price;

    /// <summary>Quantity at the price. In an update, zero removes the level.</summary>
    public double Quantity;
}
