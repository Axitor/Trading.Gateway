using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>ACCOUNT_CONFIG_UPDATE parser.</summary>
/// <remarks>
/// DESIGN NOTE: The event carries the leverage of a symbol or Multi-Assets mode. With neither, it is a change this version does not know:
/// it is still delivered, so the bot knows the account changed, and reported. A known part that cannot be read stops the session.
/// </remarks>
internal static class AccountConfigUpdateParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint TransactionTimeBit = 1u << 1;
    private const uint RequiredBits = (1u << 2) - 1;

    private const uint SymbolBit = 1u << 0;
    private const uint LeverageBit = 1u << 1;
    private const uint LeverageRequiredBits = (1u << 2) - 1;

    /// <summary>Reads the event. Fatal when a required field cannot be read; NonStandard when the change is of an unknown kind.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out AccountConfigUpdateModel model)
    {
        model = default;
        uint found = 0;
        bool hasChange = false;

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
                    }
                }
                else if (name.Length == 2)
                {
                    switch (name[0] << 8 | name[1])
                    {
                        case 'a' << 8 | 'c':
                            if (reader.TokenType != JsonTokenType.StartObject || !ReadLeverage(ref reader, ref model))
                            {
                                return WsDispatchResult.Fatal;
                            }

                            hasChange = true;
                            break;

                        case 'a' << 8 | 'i':
                            if (reader.TokenType != JsonTokenType.StartObject || !ReadAccountInfo(ref reader, ref model))
                            {
                                return WsDispatchResult.Fatal;
                            }

                            hasChange = true;
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

        return hasChange ? WsDispatchResult.Handled : WsDispatchResult.NonStandard;
    }

    /// <summary>Reads the leverage object the reader is on, up to its closing brace. False when a field is missing.</summary>
    private static bool ReadLeverage(ref Utf8JsonReader reader, ref AccountConfigUpdateModel model)
    {
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
                        found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? SymbolBit : 0;
                        break;

                    case (byte)'l':
                        found |= BinanceJsonFieldReader.TryReadInt32(ref reader, out model.Leverage) ? LeverageBit : 0;
                        break;
                }
            }

            reader.Skip();
        }

        return (found & LeverageRequiredBits) == LeverageRequiredBits;
    }

    /// <summary>Reads the account object the reader is on, up to its closing brace. False when the mode is missing.</summary>
    private static bool ReadAccountInfo(ref Utf8JsonReader reader, ref AccountConfigUpdateModel model)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (name.Length == 1 && name[0] == (byte)'j')
            {
                model.IsMultiAssetsModeChange = BinanceJsonFieldReader.TryReadBoolean(ref reader, out model.IsMultiAssetsMode);
            }

            reader.Skip();
        }

        return model.IsMultiAssetsModeChange;
    }
}
