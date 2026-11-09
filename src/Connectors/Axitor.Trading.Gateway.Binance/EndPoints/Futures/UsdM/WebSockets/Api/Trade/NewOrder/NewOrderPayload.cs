using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewOrder;

/// <summary>New Order Endpoint PayLoad Writer.</summary>
internal readonly struct NewOrderPayload : IWsApiRequestWriter
{
    /// <summary>The order, already rounded and checked.</summary>
    public NewOrderRequest Request { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "order.place"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder)
    {
        // PERF: one copy; reading fields through the property would copy the whole request each time.
        NewOrderRequest request = Request;

        if (0 < request.GoodTillDate)
        {
            builder.AppendInteger("goodTillDate"u8, request.GoodTillDate);
        }

        builder.AppendClientOrderId("newClientOrderId"u8, in request.ClientOrderId);

        if (request.ResponseType == OrderResponseType.RESULT)
        {
            builder.AppendText("newOrderRespType"u8, request.ResponseType.ToUtf8Bytes());
        }

        builder.AppendText("positionSide"u8, request.PositionSide.ToUtf8Bytes());

        if (0 < request.Price)
        {
            builder.AppendDecimal("price"u8, request.Price);
        }

        if (request.PriceMatch != PriceMatch.UNKNOWN)
        {
            builder.AppendText("priceMatch"u8, request.PriceMatch.ToUtf8Bytes());
        }

        if (0 < request.Quantity)
        {
            builder.AppendDecimal("quantity"u8, request.Quantity);
        }

        // Hedge mode rejects any reduceOnly, so false is never sent.
        if (request.IsReduceOnly)
        {
            builder.AppendBoolean("reduceOnly"u8, true);
        }

        if (request.SelfTradePreventionMode != SelfTradePreventionMode.UNKNOWN)
        {
            builder.AppendText("selfTradePreventionMode"u8, request.SelfTradePreventionMode.ToUtf8Bytes());
        }

        builder.AppendText("side"u8, request.Side.ToUtf8Bytes());
        builder.AppendSymbol("symbol"u8, request.Symbol);

        if (request.TimeInForce != TimeInForce.UNKNOWN)
        {
            builder.AppendText("timeInForce"u8, request.TimeInForce.ToUtf8Bytes());
        }

        builder.AppendText("type"u8, request.Type.ToUtf8Bytes());
    }
}
