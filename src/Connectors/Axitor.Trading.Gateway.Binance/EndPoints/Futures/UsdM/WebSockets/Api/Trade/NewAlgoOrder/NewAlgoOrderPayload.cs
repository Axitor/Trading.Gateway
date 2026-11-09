using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewAlgoOrder;

/// <summary>New Algo Order Endpoint PayLoad Writer.</summary>
internal readonly struct NewAlgoOrderPayload : IWsApiRequestWriter
{
    /// <summary>The algo order, already rounded and checked.</summary>
    public NewAlgoOrderRequest Request { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "algoOrder.place"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder)
    {
        // PERF: one copy; reading fields through the property would copy the whole request each time.
        NewAlgoOrderRequest request = Request;

        if (0 < request.ActivatePrice)
        {
            builder.AppendDecimal("activatePrice"u8, request.ActivatePrice);
        }

        builder.AppendText("algoType"u8, "CONDITIONAL"u8);

        if (0 < request.CallbackRate)
        {
            builder.AppendDecimal("callbackRate"u8, request.CallbackRate);
        }

        builder.AppendClientOrderId("clientAlgoId"u8, in request.ClientAlgoId);

        // Close-all sends neither quantity nor reduceOnly.
        if (request.IsClosePosition)
        {
            builder.AppendBoolean("closePosition"u8, true);
        }

        if (0 < request.GoodTillDate)
        {
            builder.AppendInteger("goodTillDate"u8, request.GoodTillDate);
        }

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

        if (request.IsPriceProtect)
        {
            builder.AppendBoolean("priceProtect"u8, true);
        }

        if (!request.IsClosePosition && 0 < request.Quantity)
        {
            builder.AppendDecimal("quantity"u8, request.Quantity);
        }

        // Hedge mode rejects any reduceOnly, so false is never sent.
        if (!request.IsClosePosition && request.IsReduceOnly)
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

        if (0 < request.TriggerPrice)
        {
            builder.AppendDecimal("triggerPrice"u8, request.TriggerPrice);
        }

        builder.AppendText("type"u8, request.Type.ToUtf8Bytes());

        if (request.WorkingType != WorkingType.UNKNOWN)
        {
            builder.AppendText("workingType"u8, request.WorkingType.ToUtf8Bytes());
        }
    }
}
