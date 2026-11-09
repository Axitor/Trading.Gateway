using System.Text.Json;
using System.Text.Json.Serialization;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Core.Models.Converters;

/// <summary>Reads and writes a ClientOrderId as a JSON string. An invalid value reads as ClientOrderId.Empty.</summary>
internal sealed class ClientOrderIdConverter : JsonConverter<ClientOrderId>
{
    /// <inheritdoc/>
    public override ClientOrderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }

            return ClientOrderId.Empty;
        }

        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            if (ClientOrderId.TryParse(reader.ValueSpan, out ClientOrderId parsed))
            {
                return parsed;
            }

            return ClientOrderId.Empty;
        }

        long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        // Escaping only shortens the decoded value, so the raw length is a safe upper bound.
        if (ClientOrderId.MaxLength < length)
        {
            return ClientOrderId.Empty;
        }

        Span<byte> scratch = stackalloc byte[ClientOrderId.MaxLength];
        int written = reader.CopyString(scratch);

        if (ClientOrderId.TryParse(scratch[..written], out ClientOrderId copied))
        {
            return copied;
        }

        return ClientOrderId.Empty;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ClientOrderId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> scratch = stackalloc byte[ClientOrderId.MaxLength];
        value.TryWriteTo(scratch, out int written);

        writer.WriteStringValue(scratch[..written]);
    }
}
