using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>Spot listStatus parser. Fills a reused array; a full one is replaced by one twice as large.</summary>
/// <remarks>
/// DESIGN NOTE: Every field is required: a field that cannot be read means the exchange changed its format, so the session stops.
/// An unknown enum value or a client order id that does not fit is still delivered and reported.
/// </remarks>
internal static class SpotListStatusParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint SymbolBit = 1u << 1;
    private const uint OrderListIdBit = 1u << 2;
    private const uint ContingencyTypeBit = 1u << 3;
    private const uint ListStatusTypeBit = 1u << 4;
    private const uint ListOrderStatusBit = 1u << 5;
    private const uint RejectReasonBit = 1u << 6;
    private const uint ListClientOrderIdBit = 1u << 7;
    private const uint TransactionTimeBit = 1u << 8;
    private const uint OrdersBit = 1u << 9;
    private const uint HeaderRequiredBits = (1u << 10) - 1;

    private const uint OrderSymbolBit = 1u << 0;
    private const uint OrderIdBit = 1u << 1;
    private const uint OrderClientOrderIdBit = 1u << 2;
    private const uint OrderRequiredBits = (1u << 3) - 1;

    /// <summary>Reads the event into the header and the array. Fatal when a field cannot be read; NonStandard when a value was not recognized or did not fit.</summary>
    public static WsDispatchResult Read(
        ReadOnlySpan<byte> data,
        out SpotListStatusHeader header,
        ref SpotListStatusOrder[] orders,
        out int orderCount)
    {
        header = default;
        orderCount = 0;
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
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out header.EventTime) ? EventTimeBit : 0;
                            break;

                        case (byte)'s':
                            found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out header.Symbol) ? SymbolBit : 0;
                            break;

                        case (byte)'g':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out header.OrderListId) ? OrderListIdBit : 0;
                            break;

                        case (byte)'c':
                            found |= BinanceJsonFieldReader.TryReadSpotContingencyType(ref reader, out header.ContingencyType, ref nonStandard) ? ContingencyTypeBit : 0;
                            break;

                        case (byte)'l':
                            found |= BinanceJsonFieldReader.TryReadSpotListStatusType(ref reader, out header.ListStatusType, ref nonStandard) ? ListStatusTypeBit : 0;
                            break;

                        case (byte)'L':
                            found |= BinanceJsonFieldReader.TryReadSpotListOrderStatus(ref reader, out header.ListOrderStatus, ref nonStandard) ? ListOrderStatusBit : 0;
                            break;

                        case (byte)'r':
                            found |= BinanceJsonFieldReader.TryReadRejectReason(ref reader, out header.RejectReason) ? RejectReasonBit : 0;
                            break;

                        case (byte)'C':
                            found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out header.ListClientOrderId, ref nonStandard) ? ListClientOrderIdBit : 0;
                            break;

                        case (byte)'T':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out header.TransactionTime) ? TransactionTimeBit : 0;
                            break;

                        case (byte)'O':
                            if (reader.TokenType != JsonTokenType.StartArray || !ReadOrders(ref reader, ref orders, ref orderCount, ref nonStandard))
                            {
                                return WsDispatchResult.Fatal;
                            }

                            found |= OrdersBit;
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

        if ((found & HeaderRequiredBits) != HeaderRequiredBits)
        {
            return WsDispatchResult.Fatal;
        }

        return nonStandard ? WsDispatchResult.NonStandard : WsDispatchResult.Handled;
    }

    /// <summary>Reads the order array the reader is on. False when an element misses a field.</summary>
    private static bool ReadOrders(ref Utf8JsonReader reader, ref SpotListStatusOrder[] orders, ref int count, ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == orders.Length)
            {
                orders = Grow(orders);
            }

            ref SpotListStatusOrder item = ref orders[count];
            item = default;
            uint found = 0;

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
                    switch (name[0])
                    {
                        case (byte)'s':
                            found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out item.Symbol) ? OrderSymbolBit : 0;
                            break;

                        case (byte)'i':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out item.OrderId) ? OrderIdBit : 0;
                            break;

                        case (byte)'c':
                            found |= BinanceJsonFieldReader.TryReadClientOrderId(ref reader, out item.ClientOrderId, ref nonStandard) ? OrderClientOrderIdBit : 0;
                            break;
                    }
                }

                reader.Skip();
            }

            if ((found & OrderRequiredBits) != OrderRequiredBits)
            {
                return false;
            }

            count++;
        }

        return reader.TokenType == JsonTokenType.EndArray;
    }

    /// <summary>Cold path. A larger array for a list with more orders than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] items)
    {
        T[] larger = new T[Math.Max(4, items.Length * 2)];
        items.CopyTo(larger, 0);
        return larger;
    }
}
