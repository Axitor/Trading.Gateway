using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Parsing;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.KlinesData;

/// <summary>Klines/Candlestick Data Endpoint Parser. Writes the candles into the caller's buffer and returns how many.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: The body is an array of arrays with no field names, read by position in one pass straight from UTF-8.
/// Prices and volumes go through the same decimal parser as the streams, so a price reads to the same double on both.
/// </para>
/// <para>
/// DESIGN NOTE: The exchange writes rows compactly: times and counts bare, prices and volumes quoted. Any other layout,
/// or more candles than the buffer holds, reads as malformed rather than being guessed at.
/// </para>
/// </remarks>
internal readonly struct KlinesDataParser : IResponseParser<int>
{
    // A typical row with some room, so the body buffer rarely grows.
    private const int BytesPerKline = 192;

    private readonly Memory<Kline> _destination;

    public KlinesDataParser(Memory<Kline> destination) => _destination = destination;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken, _destination.Length * BytesPerKline).ConfigureAwait(false);

        try
        {
            return Parse(read.Buffer.AsSpan(0, read.Length), _destination.Span);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static int Parse(ReadOnlySpan<byte> body, Span<Kline> destination)
    {
        int position = 0;

        if (!TryExpect(body, ref position, (byte)'['))
        {
            ThrowMalformed();
        }

        if (TryExpect(body, ref position, (byte)']'))
        {
            return 0;
        }

        int count = 0;

        while (true)
        {
            if (count == destination.Length || !TryReadKline(body, ref position, ref destination[count]))
            {
                ThrowMalformed();
            }

            count++;

            if (TryExpect(body, ref position, (byte)']'))
            {
                return count;
            }

            if (!TryExpect(body, ref position, (byte)','))
            {
                ThrowMalformed();
            }
        }
    }

    /// <summary>Reads one row and moves past its closing bracket. Fields after the taker buy quote volume are skipped.</summary>
    private static bool TryReadKline(ReadOnlySpan<byte> body, ref int position, ref Kline kline)
    {
        int index = position;

        if (!TryExpect(body, ref index, (byte)'[')
            || !TryReadInteger(body, ref index, out kline.OpenTime)
            || !TryReadNextQuoted(body, ref index, out kline.Open)
            || !TryReadNextQuoted(body, ref index, out kline.High)
            || !TryReadNextQuoted(body, ref index, out kline.Low)
            || !TryReadNextQuoted(body, ref index, out kline.Close)
            || !TryReadNextQuoted(body, ref index, out kline.Volume)
            || !TryReadNextInteger(body, ref index, out kline.CloseTime)
            || !TryReadNextQuoted(body, ref index, out kline.QuoteVolume)
            || !TryReadNextInteger(body, ref index, out long tradeCount)
            || (ulong)tradeCount > int.MaxValue
            || !TryReadNextQuoted(body, ref index, out kline.TakerBuyVolume)
            || !TryReadNextQuoted(body, ref index, out kline.TakerBuyQuoteVolume))
        {
            return false;
        }

        kline.TradeCount = (int)tradeCount;

        // The trailing "ignore" field holds no bracket.
        int end = body[index..].IndexOf((byte)']');

        if (end < 0)
        {
            return false;
        }

        position = index + end + 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadNextInteger(ReadOnlySpan<byte> body, ref int index, out long value)
    {
        if (!TryExpect(body, ref index, (byte)','))
        {
            value = 0;
            return false;
        }

        return TryReadInteger(body, ref index, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadInteger(ReadOnlySpan<byte> body, ref int index, out long value)
    {
        if (!Utf8Parser.TryParse(body[index..], out value, out int consumed))
        {
            return false;
        }

        index += consumed;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadNextQuoted(ReadOnlySpan<byte> body, ref int index, out double value)
    {
        value = 0;

        if (!TryExpect(body, ref index, (byte)',')
            || !TryExpect(body, ref index, (byte)'"')
            || !FastDecimalParser.TryParse(body[index..], out value, out int consumed))
        {
            return false;
        }

        index += consumed;
        return TryExpect(body, ref index, (byte)'"');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryExpect(ReadOnlySpan<byte> body, ref int index, byte expected)
    {
        if ((uint)index < (uint)body.Length && body[index] == expected)
        {
            index++;
            return true;
        }

        return false;
    }

    /// <summary>Cold path. The one exception type the transport already classifies as a parse failure.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Kline response did not have the expected shape.");
}
