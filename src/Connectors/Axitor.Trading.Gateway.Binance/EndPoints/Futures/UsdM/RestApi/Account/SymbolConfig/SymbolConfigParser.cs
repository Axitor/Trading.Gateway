using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;

/// <summary>Symbol Configuration Endpoint Parser.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: A field that cannot be read throws: a wrong leverage or margin type would mislead risk checks.
/// An unknown margin type reads as UNKNOWN, and a symbol whose name does not fit Symbol is left out.
/// </para>
/// <para>
/// DESIGN NOTE: Entries are collected in a pooled buffer and copied out once at their exact size, so no growing list allocates twice the data.
/// </para>
/// </remarks>
internal readonly struct SymbolConfigParser : IResponseParser<SymbolConfigModel>
{
    // Above the number of listed symbols, so the buffer rarely grows.
    private const int InitialSymbols = 1024;

    private const uint MarginTypeBit = 1u << 0;
    private const uint IsAutoAddMarginBit = 1u << 1;
    private const uint LeverageBit = 1u << 2;
    private const uint MaxNotionalValueBit = 1u << 3;
    private const uint RequiredBits = (1u << 4) - 1;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<SymbolConfigModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return Parse(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static SymbolConfigModel Parse(ReadOnlySpan<byte> body)
    {
        Utf8JsonReader reader = new(body);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            ThrowMalformed();
        }

        SymbolConfigEntry[] scratch = ArrayPool<SymbolConfigEntry>.Shared.Rent(InitialSymbols);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                if (count == scratch.Length)
                {
                    scratch = Grow(scratch);
                }

                if (TryReadEntry(ref reader, out scratch[count]))
                {
                    count++;
                }
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                ThrowMalformed();
            }

            return new SymbolConfigModel() { Symbols = scratch.AsSpan(0, count).ToArray() };
        }
        finally
        {
            ArrayPool<SymbolConfigEntry>.Shared.Return(scratch);
        }
    }

    /// <summary>Reads the entry object the reader is on, up to its closing brace. False when its name does not fit Symbol.</summary>
    private static bool TryReadEntry(ref Utf8JsonReader reader, out SymbolConfigEntry entry)
    {
        entry = default;
        uint found = 0;
        bool hasSymbol = false;
        bool fits = false;

        // A response has no telemetry path: an unknown margin type just reads as UNKNOWN.
        bool nonStandard = false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("symbol"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.String)
                {
                    ThrowMalformed();
                }

                hasSymbol = true;
                fits = BinanceJsonFieldReader.TryReadSymbol(ref reader, out entry.Symbol);
            }
            else if (reader.ValueTextEquals("marginType"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadMarginType(ref reader, out entry.MarginType, ref nonStandard) ? MarginTypeBit : 0;
            }
            else if (reader.ValueTextEquals("isAutoAddMargin"u8))
            {
                reader.Read();
                found |= TryReadFlag(ref reader, out entry.IsAutoAddMargin) ? IsAutoAddMarginBit : 0;
            }
            else if (reader.ValueTextEquals("leverage"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadInt32(ref reader, out entry.Leverage) ? LeverageBit : 0;
            }
            else if (reader.ValueTextEquals("maxNotionalValue"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out entry.MaxNotionalValue) ? MaxNotionalValueBit : 0;
            }
            else
            {
                reader.Read();
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        if (!hasSymbol || (found & RequiredBits) != RequiredBits)
        {
            ThrowMalformed();
        }

        return fits;
    }

    /// <summary>Reads a flag sent as a boolean or, as on some routes, a quoted boolean.</summary>
    private static bool TryReadFlag(ref Utf8JsonReader reader, out bool value)
    {
        if (BinanceJsonFieldReader.TryReadBoolean(ref reader, out value))
        {
            return true;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.ValueTextEquals("true"u8))
            {
                value = true;
                return true;
            }

            if (reader.ValueTextEquals("false"u8))
            {
                value = false;
                return true;
            }
        }

        value = false;
        return false;
    }

    /// <summary>Cold path. A larger pooled buffer for more symbols than any answer before.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SymbolConfigEntry[] Grow(SymbolConfigEntry[] scratch)
    {
        SymbolConfigEntry[] larger = ArrayPool<SymbolConfigEntry>.Shared.Rent(scratch.Length * 2);
        scratch.CopyTo(larger, 0);
        ArrayPool<SymbolConfigEntry>.Shared.Return(scratch);
        return larger;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Symbol configuration response did not have the expected shape.");
}
