using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>MARGIN_CALL parser. Fills a reused array; a full one is replaced by one twice as large.</summary>
/// <remarks>
/// DESIGN NOTE: A required field that cannot be read means the exchange changed its format, so the session stops.
/// No position list leaves an empty span: the warning itself still comes.
/// </remarks>
internal static class MarginCallParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint HeaderRequiredBits = (1u << 1) - 1;

    private const uint SymbolBit = 1u << 0;
    private const uint SideBit = 1u << 1;
    private const uint QuantityBit = 1u << 2;
    private const uint MarginTypeBit = 1u << 3;
    private const uint MarkPriceBit = 1u << 4;
    private const uint UnrealizedProfitBit = 1u << 5;
    private const uint MaintenanceMarginBit = 1u << 6;
    private const uint PositionRequiredBits = (1u << 7) - 1;

    /// <summary>Reads the event into the header and the array. Fatal when a required field cannot be read; NonStandard when a value was not recognized.</summary>
    public static WsDispatchResult Read(
        ReadOnlySpan<byte> data,
        out MarginCallHeader header,
        ref MarginCallPosition[] positions,
        out int positionCount)
    {
        header = default;
        positionCount = 0;
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

                        case (byte)'p':
                            if (reader.TokenType != JsonTokenType.StartArray || !ReadPositions(ref reader, ref positions, ref positionCount, ref nonStandard))
                            {
                                return WsDispatchResult.Fatal;
                            }
                            break;
                    }
                }
                else if (name.Length == 2 && name[0] == (byte)'c' && name[1] == (byte)'w')
                {
                    if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out header.CrossWalletBalance))
                    {
                        nonStandard = true;
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

    /// <summary>Reads the position array the reader is on. False when an element misses a required field.</summary>
    private static bool ReadPositions(ref Utf8JsonReader reader, ref MarginCallPosition[] positions, ref int count, ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == positions.Length)
            {
                positions = Grow(positions);
            }

            ref MarginCallPosition item = ref positions[count];
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

                if (name.Length == 1 && name[0] == (byte)'s')
                {
                    found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out item.Symbol) ? SymbolBit : 0;
                }
                else if (name.Length == 2)
                {
                    switch (name[0] << 8 | name[1])
                    {
                        case 'p' << 8 | 's':
                            found |= BinanceJsonFieldReader.TryReadPositionSide(ref reader, out item.Side) ? SideBit : 0;
                            break;

                        case 'p' << 8 | 'a':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.Quantity) ? QuantityBit : 0;
                            break;

                        case 'm' << 8 | 't':
                            found |= BinanceJsonFieldReader.TryReadMarginType(ref reader, out item.MarginType, ref nonStandard) ? MarginTypeBit : 0;
                            break;

                        case 'm' << 8 | 'p':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.MarkPrice) ? MarkPriceBit : 0;
                            break;

                        case 'u' << 8 | 'p':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.UnrealizedProfit) ? UnrealizedProfitBit : 0;
                            break;

                        case 'm' << 8 | 'm':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.MaintenanceMargin) ? MaintenanceMarginBit : 0;
                            break;

                        case 'i' << 8 | 'w':
                            if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out item.IsolatedWallet))
                            {
                                nonStandard = true;
                            }
                            break;
                    }
                }

                reader.Skip();
            }

            if ((found & PositionRequiredBits) != PositionRequiredBits)
            {
                return false;
            }

            count++;
        }

        return reader.TokenType == JsonTokenType.EndArray;
    }

    /// <summary>Cold path. A larger array for a call with more positions than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] items)
    {
        T[] larger = new T[Math.Max(4, items.Length * 2)];
        items.CopyTo(larger, 0);
        return larger;
    }
}
