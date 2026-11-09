using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Models.Orders;

/// <summary>An algo order the exchange accepted to cancel.</summary>
public struct AlgoOrderCancelModel
{
    /// <summary>Client algo id. Empty when it does not fit.</summary>
    public ClientOrderId ClientAlgoId;

    /// <summary>Algo order id.</summary>
    public long AlgoId;
}
