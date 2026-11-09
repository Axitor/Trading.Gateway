using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Depth;

/// <summary>Order book snapshot parser of spot that writes the levels into the caller's buffers. The snapshot has no event or transaction time.</summary>
internal readonly struct SpotOrderBookBufferParser : IResponseParser<OrderBookSnapshotModel>
{
    // A typical level with some room, so the body buffer rarely grows.
    private const int BytesPerLevel = 32;

    private readonly Memory<DepthLevel> _bids;
    private readonly Memory<DepthLevel> _asks;

    public SpotOrderBookBufferParser(Memory<DepthLevel> bids, Memory<DepthLevel> asks)
    {
        _bids = bids;
        _asks = asks;
    }

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<OrderBookSnapshotModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        int expectedBytes = (_bids.Length + _asks.Length) * BytesPerLevel;
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken, expectedBytes).ConfigureAwait(false);

        try
        {
            return BinanceOrderBookReader.ReadInto(read.Buffer.AsSpan(0, read.Length), hasTimes: false, _bids.Span, _asks.Span);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }
}
