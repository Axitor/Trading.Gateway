using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;

namespace Axitor.Trading.Gateway.Binance.Parsing.Orders;

/// <summary>Reads order responses of REST and the WebSocket API. Throws JsonException when the order cannot be identified.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Only the fields that identify an order are required. A malformed or unknown value elsewhere keeps its default or reads as UNKNOWN:
/// the order is already on the exchange, and user data is the source of truth for its state.
/// </para>
/// <para>
/// DESIGN NOTE: A list throws when any order in it cannot be identified: an open order left out of it would look canceled.
/// </para>
/// </remarks>
internal static class BinanceOrderReader
{
    private const uint OrderIdBit = 1u << 0;
    private const uint OrderSymbolBit = 1u << 1;
    private const uint ClientOrderIdBit = 1u << 2;
    private const uint OrderStatusBit = 1u << 3;
    private const uint OrderRequiredBits = (1u << 4) - 1;

    private const uint AlgoIdBit = 1u << 0;
    private const uint AlgoSymbolBit = 1u << 1;
    private const uint ClientAlgoIdBit = 1u << 2;
    private const uint AlgoStatusBit = 1u << 3;
    private const uint AlgoRequiredBits = (1u << 4) - 1;

    private const uint CancelAlgoIdBit = 1u << 0;
    private const uint CancelRequiredBits = (1u << 1) - 1;

    // Starting size of a list; it grows when more orders are open.
    private const int InitialListOrders = 16;

    /// <summary>Reads the answer to a place, cancel, modify or test order request.</summary>
    public static OrderModel ReadOrder(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        return ReadOrderObject(ref reader);
    }

    /// <summary>Reads a list of orders, e.g. the answer to an open orders request.</summary>
    public static OrderModel[] ReadOrders(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            ThrowMalformed();
        }

        List<OrderModel> orders = new(InitialListOrders);

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            orders.Add(ReadOrderObject(ref reader));
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            ThrowMalformed();
        }

        return orders.ToArray();
    }

    /// <summary>Reads the answer to a place algo order request.</summary>
    public static AlgoOrderModel ReadAlgoOrder(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        return ReadAlgoOrderObject(ref reader);
    }

    /// <summary>Reads a list of algo orders, e.g. the answer to an open algo orders request.</summary>
    public static AlgoOrderModel[] ReadAlgoOrders(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            ThrowMalformed();
        }

        List<AlgoOrderModel> orders = new(InitialListOrders);

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            orders.Add(ReadAlgoOrderObject(ref reader));
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            ThrowMalformed();
        }

        return orders.ToArray();
    }

    /// <summary>Reads the answer to a cancel algo order request.</summary>
    public static AlgoOrderCancelModel ReadAlgoOrderCancel(ReadOnlySpan<byte> json)
    {
        AlgoOrderCancelModel model = default;
        uint found = 0;
        bool nonStandard = false;

        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType != JsonTokenType.Null)
            {
                if (name.SequenceEqual("algoId"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.AlgoId) ? CancelAlgoIdBit : 0;
                }
                else if (name.SequenceEqual("clientAlgoId"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientAlgoId, ref nonStandard);
                }
            }

            reader.Skip();
        }

        if ((found & CancelRequiredBits) != CancelRequiredBits)
        {
            ThrowMalformed();
        }

        return model;
    }

    /// <summary>Reads the order object the reader is on, up to its closing brace.</summary>
    private static OrderModel ReadOrderObject(ref Utf8JsonReader reader)
    {
        OrderModel model = default;
        uint found = 0;

        // A response has no telemetry path: an unknown value just reads as UNKNOWN.
        bool nonStandard = false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType != JsonTokenType.Null)
            {
                ReadOrderField(ref reader, name, ref model, ref found, ref nonStandard);
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        if ((found & OrderRequiredBits) != OrderRequiredBits)
        {
            ThrowMalformed();
        }

        return model;
    }

    /// <summary>Reads the algo order object the reader is on, up to its closing brace.</summary>
    private static AlgoOrderModel ReadAlgoOrderObject(ref Utf8JsonReader reader)
    {
        AlgoOrderModel model = default;
        uint found = 0;
        bool nonStandard = false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType != JsonTokenType.Null)
            {
                ReadAlgoOrderField(ref reader, name, ref model, ref found, ref nonStandard);
            }

            reader.Skip();
        }

        if ((found & AlgoRequiredBits) != AlgoRequiredBits)
        {
            ThrowMalformed();
        }

        return model;
    }

    private static void ReadOrderField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref OrderModel model, ref uint found, ref bool nonStandard)
    {
        switch (name.Length)
        {
            case 4:
                if (name.SequenceEqual("type"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.Type, ref nonStandard);
                }
                else if (name.SequenceEqual("side"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadOrderSide(ref reader, out model.Side, ref nonStandard);
                }
                else if (name.SequenceEqual("time"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.CreateTime);
                }
                return;

            case 5:
                if (name.SequenceEqual("price"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Price);
                }
                return;

            case 6:
                if (name.SequenceEqual("symbol"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? OrderSymbolBit : 0;
                }
                else if (name.SequenceEqual("status"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadOrderStatus(ref reader, out model.Status, ref nonStandard) ? OrderStatusBit : 0;
                }
                return;

            case 7:
                if (name.SequenceEqual("orderId"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.OrderId) ? OrderIdBit : 0;
                }
                else if (name.SequenceEqual("origQty"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Quantity);
                }
                return;

            case 8:
                if (name.SequenceEqual("avgPrice"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.AveragePrice);
                }
                else if (name.SequenceEqual("cumQuote"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.FilledQuoteQuantity);
                }
                else if (name.SequenceEqual("origType"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.OriginalType, ref nonStandard);
                }
                else if (name.SequenceEqual("modifyId"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.ModifyId);
                }
                return;

            case 9:
                if (name.SequenceEqual("stopPrice"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.StopPrice);
                }
                else if (name.SequenceEqual("priceRate"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.CallbackRate);
                }
                return;

            case 10:
                if (name.SequenceEqual("reduceOnly"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsReduceOnly);
                }
                else if (name.SequenceEqual("priceMatch"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadPriceMatch(ref reader, out model.PriceMatch, ref nonStandard);
                }
                else if (name.SequenceEqual("updateTime"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.UpdateTime);
                }
                return;

            case 11:
                if (name.SequenceEqual("executedQty"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.FilledQuantity);
                }
                else if (name.SequenceEqual("timeInForce"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadTimeInForce(ref reader, out model.TimeInForce, ref nonStandard);
                }
                else if (name.SequenceEqual("workingType"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadWorkingType(ref reader, out model.WorkingType, ref nonStandard);
                }
                return;

            case 12:
                if (name.SequenceEqual("positionSide"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadPositionSide(ref reader, out model.PositionSide);
                }
                else if (name.SequenceEqual("priceProtect"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsPriceProtect);
                }
                else if (name.SequenceEqual("goodTillDate"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.GoodTillDate);
                }
                return;

            case 13:
                if (name.SequenceEqual("clientOrderId"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientOrderId, ref nonStandard) ? ClientOrderIdBit : 0;
                }
                else if (name.SequenceEqual("closePosition"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsClosePosition);
                }
                else if (name.SequenceEqual("activatePrice"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.ActivationPrice);
                }
                return;

            case 23:
                if (name.SequenceEqual("selfTradePreventionMode"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadSelfTradePreventionMode(ref reader, out model.SelfTradePreventionMode, ref nonStandard);
                }
                return;
        }
    }

    private static void ReadAlgoOrderField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref AlgoOrderModel model, ref uint found, ref bool nonStandard)
    {
        switch (name.Length)
        {
            case 4:
                if (name.SequenceEqual("side"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadOrderSide(ref reader, out model.Side, ref nonStandard);
                }
                return;

            case 5:
                if (name.SequenceEqual("price"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Price);
                }
                return;

            case 6:
                if (name.SequenceEqual("algoId"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.AlgoId) ? AlgoIdBit : 0;
                }
                else if (name.SequenceEqual("symbol"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? AlgoSymbolBit : 0;
                }
                return;

            case 8:
                if (name.SequenceEqual("algoType"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadAlgoType(ref reader, out model.AlgoType, ref nonStandard);
                }
                else if (name.SequenceEqual("quantity"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Quantity);
                }
                return;

            case 9:
                if (name.SequenceEqual("orderType"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.Type, ref nonStandard);
                }
                return;

            case 10:
                if (name.SequenceEqual("algoStatus"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadAlgoStatus(ref reader, out model.Status, ref nonStandard) ? AlgoStatusBit : 0;
                }
                else if (name.SequenceEqual("priceMatch"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadPriceMatch(ref reader, out model.PriceMatch, ref nonStandard);
                }
                else if (name.SequenceEqual("reduceOnly"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsReduceOnly);
                }
                else if (name.SequenceEqual("createTime"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.CreateTime);
                }
                else if (name.SequenceEqual("updateTime"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.UpdateTime);
                }
                return;

            case 11:
                if (name.SequenceEqual("timeInForce"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadTimeInForce(ref reader, out model.TimeInForce, ref nonStandard);
                }
                else if (name.SequenceEqual("workingType"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadWorkingType(ref reader, out model.WorkingType, ref nonStandard);
                }
                else if (name.SequenceEqual("triggerTime"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TriggerTime);
                }
                else if (name.SequenceEqual("actualPrice"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.ActualPrice);
                }
                return;

            case 12:
                if (name.SequenceEqual("clientAlgoId"u8))
                {
                    found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientAlgoId, ref nonStandard) ? ClientAlgoIdBit : 0;
                }
                else if (name.SequenceEqual("positionSide"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadPositionSide(ref reader, out model.PositionSide);
                }
                else if (name.SequenceEqual("triggerPrice"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.TriggerPrice);
                }
                else if (name.SequenceEqual("priceProtect"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsPriceProtect);
                }
                else if (name.SequenceEqual("callbackRate"u8))
                {
                    // Empty for orders other than trailing stops: keeps zero.
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.CallbackRate);
                }
                else if (name.SequenceEqual("goodTillDate"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadInt64(ref reader, out model.GoodTillDate);
                }
                return;

            case 13:
                if (name.SequenceEqual("closePosition"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsClosePosition);
                }
                else if (name.SequenceEqual("activatePrice"u8))
                {
                    // Empty for orders other than trailing stops: keeps zero.
                    _ = BinanceJsonFieldReader.TryReadDouble(ref reader, out model.ActivationPrice);
                }
                else if (name.SequenceEqual("actualOrderId"u8))
                {
                    // Empty until the algo order triggers: reads as zero.
                    _ = BinanceJsonFieldReader.TryReadInt64OrUnset(ref reader, out model.ActualOrderId);
                }
                return;

            case 23:
                if (name.SequenceEqual("selfTradePreventionMode"u8))
                {
                    _ = BinanceJsonFieldReader.TryReadSelfTradePreventionMode(ref reader, out model.SelfTradePreventionMode, ref nonStandard);
                }
                return;
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Order response did not have the expected shape.");
}
