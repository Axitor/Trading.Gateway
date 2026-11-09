using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>ORDER_TRADE_UPDATE parser.</summary>
/// <remarks>
/// DESIGN NOTE: A required field that cannot be read means the exchange changed its format, so the session stops: a lost fill leaves the position wrong.
/// A missing optional field keeps its default. A malformed one keeps its default too and is reported, like an unknown enum value.
/// </remarks>
internal static class OrderUpdateParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint TransactionTimeBit = 1u << 1;
    private const uint SymbolBit = 1u << 2;
    private const uint ClientOrderIdBit = 1u << 3;
    private const uint SideBit = 1u << 4;
    private const uint TypeBit = 1u << 5;
    private const uint TimeInForceBit = 1u << 6;
    private const uint QuantityBit = 1u << 7;
    private const uint PriceBit = 1u << 8;
    private const uint AveragePriceBit = 1u << 9;
    private const uint ExecutionTypeBit = 1u << 10;
    private const uint StatusBit = 1u << 11;
    private const uint OrderIdBit = 1u << 12;
    private const uint LastFilledQuantityBit = 1u << 13;
    private const uint FilledQuantityBit = 1u << 14;
    private const uint LastFilledPriceBit = 1u << 15;
    private const uint TradeTimeBit = 1u << 16;
    private const uint TradeIdBit = 1u << 17;
    private const uint IsMakerBit = 1u << 18;
    private const uint IsReduceOnlyBit = 1u << 19;
    private const uint PositionSideBit = 1u << 20;
    private const uint RealizedProfitBit = 1u << 21;
    private const uint RequiredBits = (1u << 22) - 1;

    /// <summary>Reads the event. Fatal when a required field cannot be read; NonStandard when a value was not recognized or did not fit.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out OrderUpdateModel model)
    {
        model = default;
        uint found = 0;
        bool nonStandard = false;

        try
        {
            Utf8JsonReader reader = new(data);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return WsDispatchResult.Fatal;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                ReadOnlySpan<byte> name = reader.ValueSpan;
                reader.Read();

                // A null value counts as missing.
                if (reader.TokenType == JsonTokenType.Null)
                {
                    continue;
                }

                if (name.Length == 1)
                {
                    switch (name[0])
                    {
                        case (byte)'E':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.EventTime) ? EventTimeBit : 0;
                            break;

                        case (byte)'T':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TransactionTime) ? TransactionTimeBit : 0;
                            break;

                        case (byte)'o':
                            if (reader.TokenType == JsonTokenType.StartObject)
                            {
                                ReadOrder(ref reader, ref model, ref found, ref nonStandard);
                            }
                            break;
                    }
                }

                // Steps over a field that was not read.
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            return WsDispatchResult.Fatal;
        }

        if ((found & RequiredBits) != RequiredBits)
        {
            return WsDispatchResult.Fatal;
        }

        return nonStandard ? WsDispatchResult.NonStandard : WsDispatchResult.Handled;
    }

    /// <summary>Reads the order object the reader is on, up to its closing brace.</summary>
    private static void ReadOrder(ref Utf8JsonReader reader, ref OrderUpdateModel model, ref uint found, ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType == JsonTokenType.Null)
            {
                continue;
            }

            if (name.Length == 1)
            {
                ReadOneLetterField(ref reader, name[0], ref model, ref found, ref nonStandard);
            }
            else if (name.Length == 2)
            {
                ReadTwoLetterField(ref reader, name[0] << 8 | name[1], ref model, ref found, ref nonStandard);
            }
            else if (name.SequenceEqual("gtd"u8))
            {
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.GoodTillDate))
                {
                    nonStandard = true;
                }
            }

            reader.Skip();
        }
    }

    private static void ReadOneLetterField(ref Utf8JsonReader reader, byte name, ref OrderUpdateModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case (byte)'s':
                found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? SymbolBit : 0;
                return;

            case (byte)'c':
                found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientOrderId, ref nonStandard) ? ClientOrderIdBit : 0;
                return;

            case (byte)'S':
                found |= BinanceJsonFieldReader.TryReadOrderSide(ref reader, out model.Side, ref nonStandard) ? SideBit : 0;
                return;

            case (byte)'o':
                found |= BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.Type, ref nonStandard) ? TypeBit : 0;
                return;

            case (byte)'f':
                found |= BinanceJsonFieldReader.TryReadTimeInForce(ref reader, out model.TimeInForce, ref nonStandard) ? TimeInForceBit : 0;
                return;

            case (byte)'q':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Quantity) ? QuantityBit : 0;
                return;

            case (byte)'p':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Price) ? PriceBit : 0;
                return;

            case (byte)'x':
                found |= BinanceJsonFieldReader.TryReadExecutionType(ref reader, out model.ExecutionType, ref nonStandard) ? ExecutionTypeBit : 0;
                return;

            case (byte)'X':
                found |= BinanceJsonFieldReader.TryReadOrderStatus(ref reader, out model.Status, ref nonStandard) ? StatusBit : 0;
                return;

            case (byte)'i':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.OrderId) ? OrderIdBit : 0;
                return;

            case (byte)'l':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastFilledQuantity) ? LastFilledQuantityBit : 0;
                return;

            case (byte)'z':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.FilledQuantity) ? FilledQuantityBit : 0;
                return;

            case (byte)'L':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastFilledPrice) ? LastFilledPriceBit : 0;
                return;

            case (byte)'T':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TradeTime) ? TradeTimeBit : 0;
                return;

            case (byte)'t':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TradeId) ? TradeIdBit : 0;
                return;

            case (byte)'m':
                found |= BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsMaker) ? IsMakerBit : 0;
                return;

            case (byte)'R':
                found |= BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsReduceOnly) ? IsReduceOnlyBit : 0;
                return;

            case (byte)'N':
                if (!BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.CommissionAsset))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'n':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Commission))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'b':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.BidsNotional))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'a':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.AskNotional))
                {
                    nonStandard = true;
                }
                return;
        }
    }

    private static void ReadTwoLetterField(ref Utf8JsonReader reader, int name, ref OrderUpdateModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case 'a' << 8 | 'p':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.AveragePrice) ? AveragePriceBit : 0;
                return;

            case 'p' << 8 | 's':
                found |= BinanceJsonFieldReader.TryReadPositionSide(ref reader, out model.PositionSide) ? PositionSideBit : 0;
                return;

            case 'r' << 8 | 'p':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.RealizedProfit) ? RealizedProfitBit : 0;
                return;

            case 's' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.StopPrice))
                {
                    nonStandard = true;
                }
                return;

            case 'w' << 8 | 't':
                if (!BinanceJsonFieldReader.TryReadWorkingType(ref reader, out model.WorkingType, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;

            case 'o' << 8 | 't':
                if (!BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.OriginalType, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;

            case 'c' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsClosePosition))
                {
                    nonStandard = true;
                }
                return;

            case 'A' << 8 | 'P':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.ActivationPrice))
                {
                    nonStandard = true;
                }
                return;

            case 'c' << 8 | 'r':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.CallbackRate))
                {
                    nonStandard = true;
                }
                return;

            case 'p' << 8 | 'P':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsPriceProtect))
                {
                    nonStandard = true;
                }
                return;

            case 'e' << 8 | 'r':
                if (!BinanceJsonFieldReader.TryReadExpiryReason(ref reader, out model.ExpiryReason, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;
        }
    }
}
