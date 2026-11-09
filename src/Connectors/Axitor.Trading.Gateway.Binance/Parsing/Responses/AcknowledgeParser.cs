using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Responses;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Responses;

/// <summary>Parser of answers that carry only a code and a message, e.g. cancel all or a margin type change.</summary>
/// <remarks>
/// DESIGN NOTE: A code other than 200 in a 200 answer throws JsonException, so the request reads as failed rather than done.
/// </remarks>
internal readonly struct AcknowledgeParser : IResponseParser<AcknowledgeModel>
{
    private const int SuccessCode = 200;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<AcknowledgeModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            Read(read.Buffer.AsSpan(0, read.Length));
            return default;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static void Read(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("code"u8))
            {
                reader.Read();

                if (!BinanceJsonFieldReader.TryReadInt32(ref reader, out int code) || code != SuccessCode)
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
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Acknowledge response did not have the expected shape.");
}
