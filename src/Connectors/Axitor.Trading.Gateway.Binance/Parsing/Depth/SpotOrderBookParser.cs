using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Depth;

/// <summary>Order book snapshot parser of spot. The snapshot has no event or transaction time.</summary>
internal readonly struct SpotOrderBookParser : IResponseParser<OrderBookModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<OrderBookModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return BinanceOrderBookReader.Read(read.Buffer.AsSpan(0, read.Length), hasTimes: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }
}
