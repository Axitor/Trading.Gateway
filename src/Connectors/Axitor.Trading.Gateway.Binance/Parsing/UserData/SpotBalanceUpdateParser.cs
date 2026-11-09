using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.UserData;

/// <summary>Spot balanceUpdate parser. Every field is required: a lost delta leaves the balance wrong.</summary>
internal static class SpotBalanceUpdateParser
{
    private const uint EventTimeBit = 1u << 0;
    private const uint AssetBit = 1u << 1;
    private const uint DeltaBit = 1u << 2;
    private const uint ClearTimeBit = 1u << 3;
    private const uint RequiredBits = (1u << 4) - 1;

    /// <summary>Reads the event. Fatal when a field cannot be read.</summary>
    public static WsDispatchResult Read(ReadOnlySpan<byte> data, out SpotBalanceUpdateModel model)
    {
        model = default;
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
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.EventTime) ? EventTimeBit : 0;
                            break;

                        case (byte)'a':
                            found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Asset) ? AssetBit : 0;
                            break;

                        case (byte)'d':
                            found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.Delta) ? DeltaBit : 0;
                            break;

                        case (byte)'T':
                            found |= BinanceJsonFieldReader.TryReadInt64(ref reader, out model.ClearTime) ? ClearTimeBit : 0;
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

        return (found & RequiredBits) == RequiredBits ? WsDispatchResult.Handled : WsDispatchResult.Fatal;
    }
}
