using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>ACCOUNT_UPDATE parser. Fills reused arrays; a full one is replaced by one twice as large.</summary>
/// <remarks>
/// DESIGN NOTE: A required field that cannot be read means the exchange changed its format, so the session stops: a lost update leaves balances and positions wrong.
/// A missing optional field keeps its default. A malformed one keeps its default too and is reported, like an unknown enum value.
/// </remarks>
internal static class AccountUpdateParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint TransactionTimeBit = 1u << 1;
    private const uint ReasonBit = 1u << 2;
    private const uint HeaderRequiredBits = (1u << 3) - 1;

    private const uint AssetBit = 1u << 0;
    private const uint WalletBalanceBit = 1u << 1;
    private const uint CrossWalletBalanceBit = 1u << 2;
    private const uint BalanceRequiredBits = (1u << 3) - 1;

    private const uint SymbolBit = 1u << 0;
    private const uint QuantityBit = 1u << 1;
    private const uint EntryPriceBit = 1u << 2;
    private const uint UnrealizedProfitBit = 1u << 3;
    private const uint MarginTypeBit = 1u << 4;
    private const uint IsolatedWalletBit = 1u << 5;
    private const uint SideBit = 1u << 6;
    private const uint PositionRequiredBits = (1u << 7) - 1;

    /// <summary>
    /// Reads the event into the header and the arrays. Fatal when a required field cannot be read; NonStandard when a value was not recognized or did not fit.
    /// </summary>
    public static WsDispatchResult Read(
        ReadOnlySpan<byte> data,
        out AccountUpdateHeader header,
        ref AccountUpdateBalance[] balances,
        out int balanceCount,
        ref AccountUpdatePosition[] positions,
        out int positionCount)
    {
        header = default;
        balanceCount = 0;
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

                        case (byte)'T':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out header.TransactionTime) ? TransactionTimeBit : 0;
                            break;

                        case (byte)'a':
                            if (reader.TokenType == JsonTokenType.StartObject
                                && !ReadUpdate(ref reader, ref header, ref balances, ref balanceCount, ref positions, ref positionCount, ref found, ref nonStandard))
                            {
                                return WsDispatchResult.Fatal;
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

        if ((found & HeaderRequiredBits) != HeaderRequiredBits)
        {
            return WsDispatchResult.Fatal;
        }

        return nonStandard ? WsDispatchResult.NonStandard : WsDispatchResult.Handled;
    }

    /// <summary>Reads the update object the reader is on, up to its closing brace. False when a balance or a position cannot be read.</summary>
    private static bool ReadUpdate(
        ref Utf8JsonReader reader,
        ref AccountUpdateHeader header,
        ref AccountUpdateBalance[] balances,
        ref int balanceCount,
        ref AccountUpdatePosition[] positions,
        ref int positionCount,
        ref uint found,
        ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType == JsonTokenType.Null || name.Length != 1)
            {
                reader.Skip();
                continue;
            }

            switch (name[0])
            {
                case (byte)'m':
                    found |= BinanceJsonFieldReader.TryReadAccountUpdateReason(ref reader, out header.Reason, ref nonStandard) ? ReasonBit : 0;
                    break;

                case (byte)'S':
                    if (!BinanceJsonFieldReader.TryReadSymbol(ref reader, out header.FundingSymbol))
                    {
                        nonStandard = true;
                    }
                    break;

                case (byte)'B':
                    if (reader.TokenType != JsonTokenType.StartArray || !ReadBalances(ref reader, ref balances, ref balanceCount, ref nonStandard))
                    {
                        return false;
                    }
                    break;

                case (byte)'P':
                    if (reader.TokenType != JsonTokenType.StartArray || !ReadPositions(ref reader, ref positions, ref positionCount, ref nonStandard))
                    {
                        return false;
                    }
                    break;
            }

            reader.Skip();
        }

        return true;
    }

    /// <summary>Reads the balance array the reader is on. False when an element misses a required field.</summary>
    private static bool ReadBalances(ref Utf8JsonReader reader, ref AccountUpdateBalance[] balances, ref int count, ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == balances.Length)
            {
                balances = Grow(balances);
            }

            ref AccountUpdateBalance item = ref balances[count];
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

                if (name.Length == 1 && name[0] == (byte)'a')
                {
                    found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out item.Asset) ? AssetBit : 0;
                }
                else if (name.Length == 2)
                {
                    switch (name[0] << 8 | name[1])
                    {
                        case 'w' << 8 | 'b':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.WalletBalance) ? WalletBalanceBit : 0;
                            break;

                        case 'c' << 8 | 'w':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.CrossWalletBalance) ? CrossWalletBalanceBit : 0;
                            break;

                        case 'b' << 8 | 'c':
                            if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out item.BalanceChange))
                            {
                                nonStandard = true;
                            }
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

    /// <summary>Reads the position array the reader is on. False when an element misses a required field.</summary>
    private static bool ReadPositions(ref Utf8JsonReader reader, ref AccountUpdatePosition[] positions, ref int count, ref bool nonStandard)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == positions.Length)
            {
                positions = Grow(positions);
            }

            ref AccountUpdatePosition item = ref positions[count];
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
                        case 'p' << 8 | 'a':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.Quantity) ? QuantityBit : 0;
                            break;

                        case 'e' << 8 | 'p':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.EntryPrice) ? EntryPriceBit : 0;
                            break;

                        case 'u' << 8 | 'p':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.UnrealizedProfit) ? UnrealizedProfitBit : 0;
                            break;

                        case 'm' << 8 | 't':
                            found |= BinanceJsonFieldReader.TryReadMarginType(ref reader, out item.MarginType, ref nonStandard) ? MarginTypeBit : 0;
                            break;

                        case 'i' << 8 | 'w':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out item.IsolatedWallet) ? IsolatedWalletBit : 0;
                            break;

                        case 'p' << 8 | 's':
                            found |= BinanceJsonFieldReader.TryReadPositionSide(ref reader, out item.Side) ? SideBit : 0;
                            break;

                        case 'c' << 8 | 'r':
                            if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out item.AccumulatedRealized))
                            {
                                nonStandard = true;
                            }
                            break;
                    }
                }
                else if (name.SequenceEqual("bep"u8))
                {
                    if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out item.BreakevenPrice))
                    {
                        nonStandard = true;
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

    /// <summary>Cold path. A larger array for an update with more items than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] items)
    {
        T[] larger = new T[Math.Max(4, items.Length * 2)];
        items.CopyTo(larger, 0);
        return larger;
    }
}
