using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewAlgoOrder;

/// <summary>New Algo Order Endpoint PayLoad Writer.</summary>
internal readonly struct NewAlgoOrderPayload : IPayloadWriter
{
    public byte[] Host { get; init; }

    /// <summary>The algo order, already rounded and checked.</summary>
    public NewAlgoOrderRequest Request { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        // PERF: one copy; reading fields through the property would copy the whole request each time.
        NewAlgoOrderRequest request = Request;

        builder.Append(Host);
        builder.Append("/fapi/v1/algoOrder"u8);

        builder.Append("algoType="u8, "CONDITIONAL"u8);
        builder.Append("symbol="u8, request.Symbol);
        builder.Append("side="u8, request.Side.ToUtf8Bytes());
        builder.Append("positionSide="u8, request.PositionSide.ToUtf8Bytes());
        builder.Append("type="u8, request.Type.ToUtf8Bytes());

        if (request.TimeInForce != TimeInForce.UNKNOWN)
        {
            builder.Append("timeInForce="u8, request.TimeInForce.ToUtf8Bytes());
        }

        // Close-all sends neither quantity nor reduceOnly.
        if (request.IsClosePosition)
        {
            builder.Append("closePosition="u8, true);
        }
        else
        {
            if (0 < request.Quantity)
            {
                builder.AppendDecimal("quantity="u8, request.Quantity);
            }

            // Hedge mode rejects any reduceOnly, so false is never sent.
            if (request.IsReduceOnly)
            {
                builder.Append("reduceOnly="u8, true);
            }
        }

        if (0 < request.Price)
        {
            builder.AppendDecimal("price="u8, request.Price);
        }

        if (0 < request.TriggerPrice)
        {
            builder.AppendDecimal("triggerPrice="u8, request.TriggerPrice);
        }

        if (request.WorkingType != WorkingType.UNKNOWN)
        {
            builder.Append("workingType="u8, request.WorkingType.ToUtf8Bytes());
        }

        if (request.PriceMatch != PriceMatch.UNKNOWN)
        {
            builder.Append("priceMatch="u8, request.PriceMatch.ToUtf8Bytes());
        }

        if (request.IsPriceProtect)
        {
            builder.Append("priceProtect="u8, true);
        }

        if (0 < request.ActivatePrice)
        {
            builder.AppendDecimal("activatePrice="u8, request.ActivatePrice);
        }

        if (0 < request.CallbackRate)
        {
            builder.AppendDecimal("callbackRate="u8, request.CallbackRate);
        }

        Span<byte> id = stackalloc byte[ClientOrderId.MaxLength];
        request.ClientAlgoId.TryWriteTo(id, out int idLength);
        builder.Append("clientAlgoId="u8, id[..idLength]);

        if (request.ResponseType == OrderResponseType.RESULT)
        {
            builder.Append("newOrderRespType="u8, request.ResponseType.ToUtf8Bytes());
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
