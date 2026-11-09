using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>ALGO_UPDATE parser.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: A required field that cannot be read means the exchange changed its format, so the session stops: a lost update leaves a stop loss untracked.
/// A missing optional field keeps its default. A malformed one keeps its default too and is reported, like an unknown enum value.
/// </para>
/// <para>
/// DESIGN NOTE: Until the order triggers, the exchange sends ai as an empty string and act as "0". Both read as unset, not as malformed.
/// </para>
/// </remarks>
internal static class AlgoUpdateParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint TransactionTimeBit = 1u << 1;
    private const uint ClientAlgoIdBit = 1u << 2;
    private const uint AlgoIdBit = 1u << 3;
    private const uint SymbolBit = 1u << 4;
    private const uint SideBit = 1u << 5;
    private const uint TypeBit = 1u << 6;
    private const uint PositionSideBit = 1u << 7;
    private const uint StatusBit = 1u << 8;
    private const uint RequiredBits = (1u << 9) - 1;

    /// <summary>Reads the event. Fatal when a required field cannot be read; NonStandard when a value was not recognized or did not fit.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out AlgoUpdateModel model)
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
    private static void ReadOrder(ref Utf8JsonReader reader, ref AlgoUpdateModel model, ref uint found, ref bool nonStandard)
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
            else
            {
                ReadLongNameField(ref reader, name, ref model, ref found, ref nonStandard);
            }

            reader.Skip();
        }
    }

    private static void ReadOneLetterField(ref Utf8JsonReader reader, byte name, ref AlgoUpdateModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case (byte)'s':
                found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? SymbolBit : 0;
                return;

            case (byte)'S':
                found |= BinanceJsonFieldReader.TryReadOrderSide(ref reader, out model.Side, ref nonStandard) ? SideBit : 0;
                return;

            case (byte)'o':
                found |= BinanceJsonFieldReader.TryReadOrderType(ref reader, out model.Type, ref nonStandard) ? TypeBit : 0;
                return;

            case (byte)'X':
                found |= BinanceJsonFieldReader.TryReadAlgoStatus(ref reader, out model.Status, ref nonStandard) ? StatusBit : 0;
                return;

            case (byte)'f':
                if (!BinanceJsonFieldReader.TryReadTimeInForce(ref reader, out model.TimeInForce, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'q':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Quantity))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'p':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Price))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'R':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsReduceOnly))
                {
                    nonStandard = true;
                }
                return;
        }
    }

    private static void ReadTwoLetterField(ref Utf8JsonReader reader, int name, ref AlgoUpdateModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case 'p' << 8 | 's':
                found |= BinanceJsonFieldReader.TryReadPositionSide(ref reader, out model.PositionSide) ? PositionSideBit : 0;
                return;

            case 'a' << 8 | 't':
                if (!BinanceJsonFieldReader.TryReadAlgoType(ref reader, out model.AlgoType, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;

            case 'a' << 8 | 'i':
                if (!BinanceJsonFieldReader.TryReadInt64OrUnset(ref reader, out model.OrderId))
                {
                    nonStandard = true;
                }
                return;

            case 'a' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.AveragePrice))
                {
                    nonStandard = true;
                }
                return;

            case 'a' << 8 | 'q':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.ExecutedQuantity))
                {
                    nonStandard = true;
                }
                return;

            case 't' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.TriggerPrice))
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

            case 'c' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsClosePosition))
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

            case 't' << 8 | 't':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TriggerTime))
                {
                    nonStandard = true;
                }
                return;

            case 'r' << 8 | 'm':
                if (!BinanceJsonFieldReader.TryReadText(ref reader, out model.RejectReason))
                {
                    nonStandard = true;
                }
                return;

            case 'i' << 8 | 'a':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsActivated))
                {
                    nonStandard = true;
                }
                return;
        }
    }

    private static void ReadLongNameField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref AlgoUpdateModel model, ref uint found, ref bool nonStandard)
    {
        if (name.SequenceEqual("aid"u8))
        {
            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.AlgoId) ? AlgoIdBit : 0;
        }
        else if (name.SequenceEqual("caid"u8))
        {
            found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientAlgoId, ref nonStandard) ? ClientAlgoIdBit : 0;
        }
        else if (name.SequenceEqual("act"u8))
        {
            if (!BinanceJsonFieldReader.TryReadOrderTypeOrUnset(ref reader, out model.ActualType, ref nonStandard))
            {
                nonStandard = true;
            }
        }
        else if (name.SequenceEqual("gtd"u8))
        {
            if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.GoodTillDate))
            {
                nonStandard = true;
            }
        }
    }
}
