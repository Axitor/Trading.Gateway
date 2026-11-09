namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>Open orders of one symbol or of every symbol.</summary>
public struct OpenOrdersModel
{
    /// <summary>Open orders. Empty rather than null when there are none.</summary>
    public OrderModel[] Orders;
}
