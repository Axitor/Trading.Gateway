using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.ModifyOrder;

/// <summary>Modify Order Endpoint PayLoad Writer. Sends the order id, or the client order id when it is zero.</summary>
internal readonly struct ModifyOrderPayload : IWsApiRequestWriter
{
    public Symbol Symbol { get; init; }
    public long OrderId { get; init; }
    public ClientOrderId ClientOrderId { get; init; }
    public double Quantity { get; init; }
    public double Price { get; init; }
    public long ModifyId { get; init; }
    public OrderSide Side { get; init; }
    public bool IsReduceOnly { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "order.modify"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder)
    {
        if (ModifyId != 0)
        {
            builder.AppendInteger("modifyId"u8, ModifyId);
        }

        if (0 < OrderId)
        {
            builder.AppendInteger("orderId"u8, OrderId);
        }
        else
        {
            ClientOrderId clientOrderId = ClientOrderId;
            builder.AppendClientOrderId("origClientOrderId"u8, in clientOrderId);
        }

        builder.AppendDecimal("price"u8, Price);
        builder.AppendDecimal("quantity"u8, Quantity);

        // Only a check on the exchange; false is its default.
        if (IsReduceOnly)
        {
            builder.AppendBoolean("reduceOnly"u8, true);
        }

        builder.AppendText("side"u8, Side.ToUtf8Bytes());
        builder.AppendSymbol("symbol"u8, Symbol);
    }
}
