using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;

/// <summary>Notional and Leverage Brackets Endpoint Parser.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: The docs give the answer as an array or as a single object, so both are read.
/// </para>
/// <para>
/// DESIGN NOTE: A field that cannot be read throws: a wrong leverage cap or margin ratio would mislead risk checks.
/// A symbol whose name does not fit Symbol is left out.
/// </para>
/// <para>
/// DESIGN NOTE: Symbols go into a pooled buffer, and the levels of every symbol into one pooled buffer shared by the whole answer.
/// Each is copied out once at its exact size, so no growing list allocates twice the data.
/// </para>
/// </remarks>
internal readonly struct LeverageBracketParser : IResponseParser<LeverageBracketModel>
{
    // Above the number of listed symbols and of brackets per symbol, so the buffers rarely grow.
    private const int InitialSymbols = 1024;
    private const int InitialLevels = 32;

    // The answer for every symbol is about a megabyte: the body buffer starts at its size instead of doubling up to it.
    private const int ExpectedBodyBytes = 1536 * 1024;

    private const uint BracketBit = 1u << 0;
    private const uint InitialLeverageBit = 1u << 1;
    private const uint NotionalCapBit = 1u << 2;
    private const uint NotionalFloorBit = 1u << 3;
    private const uint MaintMarginRatioBit = 1u << 4;
    private const uint CumBit = 1u << 5;
    private const uint LevelRequiredBits = (1u << 6) - 1;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<LeverageBracketModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken, ExpectedBodyBytes).ConfigureAwait(false);

        try
        {
            return Parse(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static LeverageBracketModel Parse(ReadOnlySpan<byte> body)
    {
        Utf8JsonReader reader = new(body);

        if (!reader.Read())
        {
            ThrowMalformed();
        }

        LeverageBracketLevel[] levels = ArrayPool<LeverageBracketLevel>.Shared.Rent(InitialLevels);

        try
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                SymbolBrackets[] single = TryReadSymbol(ref reader, ref levels, out SymbolBrackets item) ? [item] : [];
                return new LeverageBracketModel() { Symbols = single };
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ThrowMalformed();
            }

            return new LeverageBracketModel() { Symbols = ReadSymbols(ref reader, ref levels) };
        }
        finally
        {
            ArrayPool<LeverageBracketLevel>.Shared.Return(levels);
        }
    }

    /// <summary>Reads the symbol array the reader is on, up to its closing bracket.</summary>
    private static SymbolBrackets[] ReadSymbols(ref Utf8JsonReader reader, ref LeverageBracketLevel[] levels)
    {
        SymbolBrackets[] scratch = ArrayPool<SymbolBrackets>.Shared.Rent(InitialSymbols);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                if (count == scratch.Length)
                {
                    scratch = Grow(scratch);
                }

                if (TryReadSymbol(ref reader, ref levels, out scratch[count]))
                {
                    count++;
                }
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                ThrowMalformed();
            }

            return scratch.AsSpan(0, count).ToArray();
        }
        finally
        {
            // Entries hold level arrays: cleared, so the pool keeps none of them alive.
            ArrayPool<SymbolBrackets>.Shared.Return(scratch, clearArray: true);
        }
    }

    /// <summary>Reads the symbol object the reader is on, up to its closing brace. False when its name does not fit Symbol.</summary>
    private static bool TryReadSymbol(ref Utf8JsonReader reader, ref LeverageBracketLevel[] levels, out SymbolBrackets item)
    {
        item = default;
        item.NotionalCoef = 1;
        bool hasSymbol = false;
        bool fits = false;
        LeverageBracketLevel[]? brackets = null;

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
                fits = BinanceJsonFieldReader.TryReadSymbol(ref reader, out item.Symbol);
            }
            else if (reader.ValueTextEquals("notionalCoef"u8))
            {
                reader.Read();

                if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out item.NotionalCoef))
                {
                    ThrowMalformed();
                }
            }
            else if (reader.ValueTextEquals("brackets"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    ThrowMalformed();
                }

                brackets = ReadLevels(ref reader, ref levels);
            }
            else
            {
                reader.Read();
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        if (!hasSymbol || brackets is null)
        {
            ThrowMalformed();
        }

        item.Brackets = brackets;
        return fits;
    }

    /// <summary>Reads the bracket array the reader is on, up to its closing bracket, through the shared level buffer.</summary>
    private static LeverageBracketLevel[] ReadLevels(ref Utf8JsonReader reader, ref LeverageBracketLevel[] levels)
    {
        int count = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            if (count == levels.Length)
            {
                levels = Grow(levels);
            }

            levels[count++] = ReadLevel(ref reader);
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            ThrowMalformed();
        }

        return levels.AsSpan(0, count).ToArray();
    }

    /// <summary>Reads one bracket object up to its closing brace.</summary>
    private static LeverageBracketLevel ReadLevel(ref Utf8JsonReader reader)
    {
        LeverageBracketLevel level = default;
        uint found = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("bracket"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadInt32(ref reader, out level.Bracket) ? BracketBit : 0;
            }
            else if (reader.ValueTextEquals("initialLeverage"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadInt32(ref reader, out level.InitialLeverage) ? InitialLeverageBit : 0;
            }
            else if (reader.ValueTextEquals("notionalCap"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out level.NotionalCap) ? NotionalCapBit : 0;
            }
            else if (reader.ValueTextEquals("notionalFloor"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out level.NotionalFloor) ? NotionalFloorBit : 0;
            }
            else if (reader.ValueTextEquals("maintMarginRatio"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out level.MaintMarginRatio) ? MaintMarginRatioBit : 0;
            }
            else if (reader.ValueTextEquals("cum"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out level.Cum) ? CumBit : 0;
            }
            else
            {
                reader.Read();
            }

            reader.Skip();
        }

        if ((found & LevelRequiredBits) != LevelRequiredBits)
        {
            ThrowMalformed();
        }

        return level;
    }

    /// <summary>Cold path. A larger pooled buffer for more items than any answer before. One holding references is cleared on return.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] scratch)
    {
        T[] larger = ArrayPool<T>.Shared.Rent(scratch.Length * 2);
        scratch.CopyTo(larger, 0);
        ArrayPool<T>.Shared.Return(scratch, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        return larger;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Leverage bracket response did not have the expected shape.");
}
