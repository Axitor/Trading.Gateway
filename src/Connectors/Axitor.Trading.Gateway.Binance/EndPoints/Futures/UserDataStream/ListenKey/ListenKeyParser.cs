using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;

/// <summary>Start User Data Stream Endpoint Parser.</summary>
internal readonly struct ListenKeyParser : IWsApiResultParser<ListenKeyModel>
{
    /// <inheritdoc />
    public static ListenKeyModel Parse(ReadOnlySpan<byte> result)
    {
        Utf8JsonReader reader = new(result);

        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("listenKey"u8))
                {
                    reader.Read();

                    // The key goes into stream names as is, so an escaped or empty one is not accepted.
                    if (reader.TokenType == JsonTokenType.String && !reader.ValueIsEscaped && !reader.ValueSpan.IsEmpty)
                    {
                        return new ListenKeyModel() { ListenKey = reader.ValueSpan.ToArray() };
                    }

                    break;
                }

                reader.Read();
                reader.Skip();
            }
        }

        ThrowMalformed();
        return default;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Listen key response did not have the expected shape.");
}
