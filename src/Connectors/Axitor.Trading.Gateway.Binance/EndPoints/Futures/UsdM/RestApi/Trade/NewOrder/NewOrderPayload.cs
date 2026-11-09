using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewOrder;

/// <summary>New Order Endpoint PayLoad Writer. Also writes the test order, which takes the same params.</summary>
internal readonly struct NewOrderPayload : IPayloadWriter
{
    public byte[] Host { get; init; }

    /// <summary>The order, already rounded and checked.</summary>
    public NewOrderRequest Request { get; init; }

    /// <summary>Sends to the test order path.</summary>
    public bool IsTest { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        // PERF: one copy; reading fields through the property would copy the whole request each time.
        NewOrderRequest request = Request;

        builder.Append(Host);
        builder.Append(IsTest ? "/fapi/v1/order/test"u8 : "/fapi/v1/order"u8);

        builder.Append("symbol="u8, request.Symbol);
        builder.Append("side="u8, request.Side.ToUtf8Bytes());
        builder.Append("positionSide="u8, request.PositionSide.ToUtf8Bytes());
        builder.Append("type="u8, request.Type.ToUtf8Bytes());

        if (request.TimeInForce != TimeInForce.UNKNOWN)
        {
            builder.Append("timeInForce="u8, request.TimeInForce.ToUtf8Bytes());
        }

        if (0 < request.Quantity)
        {
            builder.AppendDecimal("quantity="u8, request.Quantity);
        }

        if (0 < request.Price)
        {
            builder.AppendDecimal("price="u8, request.Price);
        }

        // Hedge mode rejects any reduceOnly, so false is never sent.
        if (request.IsReduceOnly)
        {
            builder.Append("reduceOnly="u8, true);
        }

        Span<byte> id = stackalloc byte[ClientOrderId.MaxLength];
        request.ClientOrderId.TryWriteTo(id, out int idLength);
        builder.Append("newClientOrderId="u8, id[..idLength]);

        if (request.ResponseType == OrderResponseType.RESULT)
        {
            builder.Append("newOrderRespType="u8, request.ResponseType.ToUtf8Bytes());
        }

        if (request.PriceMatch != PriceMatch.UNKNOWN)
        {
            builder.Append("priceMatch="u8, request.PriceMatch.ToUtf8Bytes());
        }

        if (request.SelfTradePreventionMode != SelfTradePreventionMode.UNKNOWN)
        {
            builder.Append("selfTradePreventionMode="u8, request.SelfTradePreventionMode.ToUtf8Bytes());
        }

        if (0 < request.GoodTillDate)
        {
            builder.Append("goodTillDate="u8, request.GoodTillDate);
        }
    }
}
