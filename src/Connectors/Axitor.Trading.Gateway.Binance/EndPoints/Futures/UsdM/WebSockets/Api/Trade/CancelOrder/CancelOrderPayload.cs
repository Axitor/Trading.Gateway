using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.CancelOrder;

/// <summary>Cancel Order Endpoint PayLoad Writer. Sends the order id, or the client order id when it is zero.</summary>
internal readonly struct CancelOrderPayload : IWsApiRequestWriter
{
    public Symbol Symbol { get; init; }
    public long OrderId { get; init; }
    public ClientOrderId ClientOrderId { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "order.cancel"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder)
    {
        if (0 < OrderId)
        {
            builder.AppendInteger("orderId"u8, OrderId);
        }
        else
        {
            ClientOrderId clientOrderId = ClientOrderId;
            builder.AppendClientOrderId("origClientOrderId"u8, in clientOrderId);
        }

        builder.AppendSymbol("symbol"u8, Symbol);
    }
}
