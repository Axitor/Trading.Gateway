using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>TRADE_LITE parser.</summary>
/// <remarks>
/// DESIGN NOTE: Every field is required: the event only reports a fill, and a fill with a field missing cannot be counted.
/// A field that cannot be read means the exchange changed its format, so the session stops.
/// </remarks>
internal static class TradeLiteParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint TransactionTimeBit = 1u << 1;
    private const uint SymbolBit = 1u << 2;
    private const uint QuantityBit = 1u << 3;
    private const uint PriceBit = 1u << 4;
    private const uint IsMakerBit = 1u << 5;
    private const uint ClientOrderIdBit = 1u << 6;
    private const uint SideBit = 1u << 7;
    private const uint LastFilledPriceBit = 1u << 8;
    private const uint LastFilledQuantityBit = 1u << 9;
    private const uint TradeIdBit = 1u << 10;
    private const uint OrderIdBit = 1u << 11;
    private const uint RequiredBits = (1u << 12) - 1;

    /// <summary>Reads the event. Fatal when a field cannot be read; NonStandard when a value was not recognized or did not fit.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out TradeLiteModel model)
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
                    ReadField(ref reader, name[0], ref model, ref found, ref nonStandard);
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

    private static void ReadField(ref Utf8JsonReader reader, byte name, ref TradeLiteModel model, ref uint found, ref bool nonStandard)
    {
        switch (name)
        {
            case (byte)'E':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.EventTime) ? EventTimeBit : 0;
                return;

            case (byte)'T':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TransactionTime) ? TransactionTimeBit : 0;
                return;

            case (byte)'s':
                found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? SymbolBit : 0;
                return;

            case (byte)'q':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Quantity) ? QuantityBit : 0;
                return;

            case (byte)'p':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Price) ? PriceBit : 0;
                return;

            case (byte)'m':
                found |= BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsMaker) ? IsMakerBit : 0;
                return;

            case (byte)'c':
                found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out model.ClientOrderId, ref nonStandard) ? ClientOrderIdBit : 0;
                return;

            case (byte)'S':
                found |= BinanceJsonFieldReader.TryReadOrderSide(ref reader, out model.Side, ref nonStandard) ? SideBit : 0;
                return;

            case (byte)'L':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastFilledPrice) ? LastFilledPriceBit : 0;
                return;

            case (byte)'l':
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.LastFilledQuantity) ? LastFilledQuantityBit : 0;
                return;

            case (byte)'t':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.TradeId) ? TradeIdBit : 0;
                return;

            case (byte)'i':
                found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.OrderId) ? OrderIdBit : 0;
                return;
        }
    }
}
