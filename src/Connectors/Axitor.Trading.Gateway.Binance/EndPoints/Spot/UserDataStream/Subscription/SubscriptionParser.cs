using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;

/// <summary>Subscribe and unsubscribe answer parser. Unsubscribe answers an empty object.</summary>
internal readonly struct SubscriptionParser : IWsApiResultParser<SubscriptionModel>
{
    /// <inheritdoc />
    public static SubscriptionModel Parse(ReadOnlySpan<byte> result)
    {
        Utf8JsonReader reader = new(result);
        long subscriptionId = -1;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("subscriptionId"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out subscriptionId))
                {
                    ThrowMalformed();
                }
            }
            else
            {
                reader.Read();
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        return new SubscriptionModel() { SubscriptionId = subscriptionId };
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Subscription response did not have the expected shape.");
}
