using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;

/// <summary>Change Initial Leverage Endpoint Parser. Every field is required.</summary>
internal readonly struct ChangeLeverageParser : IResponseParser<ChangeLeverageModel>
{
    private const uint SymbolBit = 1u << 0;
    private const uint LeverageBit = 1u << 1;
    private const uint MaxNotionalValueBit = 1u << 2;
    private const uint RequiredBits = (1u << 3) - 1;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ChangeLeverageModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return Read(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static ChangeLeverageModel Read(ReadOnlySpan<byte> json)
    {
        ChangeLeverageModel model = default;
        uint found = 0;

        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("symbol"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadSymbol(ref reader, out model.Symbol) ? SymbolBit : 0;
            }
            else if (reader.ValueTextEquals("leverage"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadInt32(ref reader, out model.Leverage) ? LeverageBit : 0;
            }
            else if (reader.ValueTextEquals("maxNotionalValue"u8))
            {
                reader.Read();
                found |= BinanceJsonFieldReader.TryReadDouble(ref reader, out model.MaxNotionalValue) ? MaxNotionalValueBit : 0;
            }
            else
            {
                reader.Read();
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        if ((found & RequiredBits) != RequiredBits)
        {
            ThrowMalformed();
        }

        return model;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Leverage response did not have the expected shape.");
}
