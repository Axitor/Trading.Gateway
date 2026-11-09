using System.Text.Json;
using System.Text.Json.Serialization;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Core.Models.Converters;

/// <summary>Reads and writes a Symbol as a JSON string. An invalid value reads as Symbol.Empty.</summary>
internal sealed class SymbolConverter : JsonConverter<Symbol>
{
    /// <inheritdoc/>
    public override Symbol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }

            return Symbol.Empty;
        }

        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            if (Symbol.TryParse(reader.ValueSpan, out Symbol parsed))
            {
                return parsed;
            }

            return Symbol.Empty;
        }

        long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        // Escaping only shortens the decoded value, so the raw length is a safe upper bound.
        if (Symbol.MaxLength < length)
        {
            return Symbol.Empty;
        }

        Span<byte> scratch = stackalloc byte[Symbol.MaxLength];
        int written = reader.CopyString(scratch);

        if (Symbol.TryParse(scratch[..written], out Symbol copied))
        {
            return copied;
        }

        return Symbol.Empty;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Symbol value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> scratch = stackalloc byte[Symbol.MaxLength];
        value.TryWriteTo(scratch, out int written);

        writer.WriteStringValue(scratch[..written]);
    }
}
