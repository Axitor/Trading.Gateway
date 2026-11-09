using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>Spot outboundAccountPosition parser. Fills a reused array; a full one is replaced by one twice as large.</summary>
/// <remarks>
/// DESIGN NOTE: Every field is required: a lost balance change leaves the balance wrong, and a field that cannot be read means the exchange changed its format.
/// </remarks>
internal static class SpotAccountPositionParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint LastUpdateTimeBit = 1u << 1;
    private const uint BalancesBit = 1u << 2;
    private const uint HeaderRequiredBits = (1u << 3) - 1;

    private const uint AssetBit = 1u << 0;
    private const uint FreeBit = 1u << 1;
    private const uint LockedBit = 1u << 2;
    private const uint BalanceRequiredBits = (1u << 3) - 1;

    /// <summary>Reads the event into the header and the array. Fatal when a field cannot be read.</summary>
    public static WsDispatchResult Read(
        ReadOnlySpan<byte> data,
        out SpotAccountPositionHeader header,
        ref SpotAccountBalance[] balances,
        out int balanceCount)
    {
        header = default;
        balanceCount = 0;
        uint found = 0;

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

                        case (byte)'u':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out header.LastUpdateTime) ? LastUpdateTimeBit : 0;
                            break;

                        case (byte)'B':
                            if (reader.TokenType != JsonTokenType.StartArray || !ReadBalances(ref reader, ref balances, ref balanceCount))
                            {
                                return WsDispatchResult.Fatal;
                            }

                            found |= BalancesBit;
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

        return (found & HeaderRequiredBits) == HeaderRequiredBits ? WsDispatchResult.Handled : WsDispatchResult.Fatal;
    }

    /// <summary>Reads the balance array the reader is on. False when an element misses a field.</summary>
    private static bool ReadBalances(ref Utf8JsonReader reader, ref SpotAccountBalance[] balances, ref int count)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == balances.Length)
            {
                balances = Grow(balances);
            }

            ref SpotAccountBalance item = ref balances[count];
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
                        case (byte)'a':
                            found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out item.Asset) ? AssetBit : 0;
                            break;

                        case (byte)'f':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.Free) ? FreeBit : 0;
                            break;

                        case (byte)'l':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.Locked) ? LockedBit : 0;
                            break;
                    }
                }

                reader.Skip();
            }

            if ((found & BalanceRequiredBits) != BalanceRequiredBits)
            {
                return false;
            }

            count++;
        }

        return reader.TokenType == JsonTokenType.EndArray;
    }

    /// <summary>Cold path. A larger array for an update with more items than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] items)
    {
        T[] larger = new T[Math.Max(4, items.Length * 2)];
        items.CopyTo(larger, 0);
        return larger;
    }
}
