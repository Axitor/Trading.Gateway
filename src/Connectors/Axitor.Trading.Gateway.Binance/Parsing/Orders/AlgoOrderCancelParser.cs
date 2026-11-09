using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Orders;

/// <summary>Algo order cancel response parser of REST and the WebSocket API.</summary>
internal readonly struct AlgoOrderCancelParser : IResponseParser<AlgoOrderCancelModel>, IWsApiResultParser<AlgoOrderCancelModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<AlgoOrderCancelModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return BinanceOrderReader.ReadAlgoOrderCancel(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    /// <inheritdoc/>
    public static AlgoOrderCancelModel Parse(ReadOnlySpan<byte> result) => BinanceOrderReader.ReadAlgoOrderCancel(result);
}
