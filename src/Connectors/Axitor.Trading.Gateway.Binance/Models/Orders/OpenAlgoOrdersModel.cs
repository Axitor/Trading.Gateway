namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>Open algo orders, e.g. stop losses and take profits, of one symbol or of every symbol.</summary>
public struct OpenAlgoOrdersModel
{
    /// <summary>Open algo orders. Empty rather than null when there are none.</summary>
    public AlgoOrderModel[] Orders;
}
