using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>Spot executionReport parser. Reads the event object, without the subscription envelope.</summary>
/// <remarks>
/// DESIGN NOTE: A field the docs always send that cannot be read means the exchange changed its format, so the session stops: a lost fill leaves the balance wrong.
/// A conditional field that is missing keeps its default; a malformed one keeps its default too and is reported, like an unknown enum value.
/// </remarks>
internal static class SpotExecutionReportParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint SymbolBit = 1u << 1;
    private const uint ClientOrderIdBit = 1u << 2;
    private const uint SideBit = 1u << 3;
    private const uint TypeBit = 1u << 4;
    private const uint TimeInForceBit = 1u << 5;
    private const uint QuantityBit = 1u << 6;
    private const uint PriceBit = 1u << 7;
    private const uint StopPriceBit = 1u << 8;
    private const uint IcebergQuantityBit = 1u << 9;
    private const uint OrderListIdBit = 1u << 10;
    private const uint OriginalClientOrderIdBit = 1u << 11;
    private const uint ExecutionTypeBit = 1u << 12;
    private const uint StatusBit = 1u << 13;
    private const uint RejectReasonBit = 1u << 14;
    private const uint OrderIdBit = 1u << 15;
    private const uint LastFilledQuantityBit = 1u << 16;
    private const uint FilledQuantityBit = 1u << 17;
    private const uint LastFilledPriceBit = 1u << 18;
    private const uint CommissionBit = 1u << 19;
    private const uint TransactionTimeBit = 1u << 20;
    private const uint TradeIdBit = 1u << 21;
    private const uint ExecutionIdBit = 1u << 22;
    private const uint IsWorkingBit = 1u << 23;
    private const uint IsMakerBit = 1u << 24;
    private const uint CreateTimeBit = 1u << 25;
    private const uint FilledQuoteQuantityBit = 1u << 26;
    private const uint LastFilledQuoteQuantityBit = 1u << 27;
    private const uint QuoteOrderQuantityBit = 1u << 28;
    private const uint SelfTradePreventionModeBit = 1u << 29;
    private const uint RequiredBits = (1u << 30) - 1;

    /// <summary>Reads the event. Fatal when a required field cannot be read; NonStandard when a value was not recognized or did not fit.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out SpotExecutionReportModel model)
    {
        model = default;

        // eR comes only with an expired order.
        model.ExpiryReason = SpotExpiryReason.NONE;

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
                    ReadOneLetterField(ref reader, name[0], ref model, ref found, ref nonStandard);
                }
                else if (name.Length == 2)
                {
                    ReadTwoLetterField(ref reader, name[0] << 8 | name[1], ref model, ref nonStandard);
                }
                else if (name.Length == 3)
                {
                    ReadThreeLetterField(ref reader, name, ref model, ref nonStandard);
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

    private static void ReadOneLetterField(ref Utf8JsonReader reader, byte name, ref SpotExecutionReportModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case (byte)'E':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.EventTime) ? EventTimeBit : 0;
                return;

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
                found |= BinanceJsonFieldReader.TryReadSpotOrderType(ref reader, out model.Type, ref nonStandard) ? TypeBit : 0;
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

            case (byte)'P':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.StopPrice) ? StopPriceBit : 0;
                return;

            case (byte)'F':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.IcebergQuantity) ? IcebergQuantityBit : 0;
                return;

            case (byte)'g':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.OrderListId) ? OrderListIdBit : 0;
                return;

            case (byte)'C':
                found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.OriginalClientOrderId, ref nonStandard) ? OriginalClientOrderIdBit : 0;
                return;

            case (byte)'x':
                found |= BinanceJsonFieldReader.TryReadSpotExecutionType(ref reader, out model.ExecutionType, ref nonStandard) ? ExecutionTypeBit : 0;
                return;

            case (byte)'X':
                found |= BinanceJsonFieldReader.TryReadSpotOrderStatus(ref reader, out model.Status, ref nonStandard) ? StatusBit : 0;
                return;

            case (byte)'r':
                found |= BinanceJsonFieldReader.TryReadRejectReason(ref reader, out model.RejectReason) ? RejectReasonBit : 0;
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

            case (byte)'n':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Commission) ? CommissionBit : 0;
                return;

            case (byte)'T':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TransactionTime) ? TransactionTimeBit : 0;
                return;

            case (byte)'t':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TradeId) ? TradeIdBit : 0;
                return;

            case (byte)'I':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.ExecutionId) ? ExecutionIdBit : 0;
                return;

            case (byte)'w':
                found |= BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsWorking) ? IsWorkingBit : 0;
                return;

            case (byte)'m':
                found |= BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsMaker) ? IsMakerBit : 0;
                return;

            case (byte)'O':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.CreateTime) ? CreateTimeBit : 0;
                return;

            case (byte)'Z':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.FilledQuoteQuantity) ? FilledQuoteQuantityBit : 0;
                return;

            case (byte)'Y':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastFilledQuoteQuantity) ? LastFilledQuoteQuantityBit : 0;
                return;

            case (byte)'Q':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.QuoteOrderQuantity) ? QuoteOrderQuantityBit : 0;
                return;

            case (byte)'V':
                found |= BinanceJsonFieldReader.TryReadSpotSelfTradePreventionMode(ref reader, out model.SelfTradePreventionMode, ref nonStandard) ? SelfTradePreventionModeBit : 0;
                return;

            case (byte)'N':
                if (!BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.CommissionAsset))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'W':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.WorkingTime))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'d':
                if (!BinanceJsonFieldReader.TryReadInt32(ref reader, out model.TrailingDelta))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'D':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TrailingTime))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'j':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.StrategyId))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'J':
                if (!BinanceJsonFieldReader.TryReadInt32(ref reader, out model.StrategyType))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'v':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.PreventedMatchId))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'A':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.PreventedQuantity))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'B':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastPreventedQuantity))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'u':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TradeGroupId))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'U':
                if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out model.CounterOrderId))
                {
                    nonStandard = true;
                }
                return;

            case (byte)'k':
                if (!BinanceJsonFieldReader.TryReadSpotWorkingFloor(ref reader, out model.WorkingFloor, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;
        }
    }

    private static void ReadTwoLetterField(ref Utf8JsonReader reader, int name, ref SpotExecutionReportModel model, ref bool nonStandard)
    {
        switch (name)
        {
            case 'C' << 8 | 's':
                if (!BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.CounterSymbol))
                {
                    nonStandard = true;
                }
                return;

            case 'p' << 8 | 'l':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.PreventedExecutionQuantity))
                {
                    nonStandard = true;
                }
                return;

            case 'p' << 8 | 'L':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.PreventedExecutionPrice))
                {
                    nonStandard = true;
                }
                return;

            case 'p' << 8 | 'Y':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.PreventedExecutionQuoteQuantity))
                {
                    nonStandard = true;
                }
                return;

            case 'u' << 8 | 'S':
                if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.UsedSor))
                {
                    nonStandard = true;
                }
                return;

            case 'g' << 8 | 'P':
                if (!BinanceJsonFieldReader.TryReadSpotPegPriceType(ref reader, out model.PegPriceType, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;

            case 'g' << 8 | 'p':
                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out model.PeggedPrice))
                {
                    nonStandard = true;
                }
                return;

            case 'e' << 8 | 'R':
                if (!BinanceJsonFieldReader.TryReadSpotExpiryReason(ref reader, out model.ExpiryReason, ref nonStandard))
                {
                    nonStandard = true;
                }
                return;
        }
    }

    private static void ReadThreeLetterField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref SpotExecutionReportModel model, ref bool nonStandard)
    {
        if (name.SequenceEqual("gOT"u8))
        {
            if (!BinanceJsonFieldReader.TryReadSpotPegOffsetType(ref reader, out model.PegOffsetType, ref nonStandard))
            {
                nonStandard = true;
            }
        }
        else if (name.SequenceEqual("gOV"u8))
        {
            if (!BinanceJsonFieldReader.TryReadInt32(ref reader, out model.PegOffsetValue))
            {
                nonStandard = true;
            }
        }
    }
}
