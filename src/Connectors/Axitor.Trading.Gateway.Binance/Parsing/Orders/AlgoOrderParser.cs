using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Orders;

/// <summary>Algo order place response parser of REST and the WebSocket API.</summary>
internal readonly struct AlgoOrderParser : IResponseParser<AlgoOrderModel>, IWsApiResultParser<AlgoOrderModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<AlgoOrderModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return BinanceOrderReader.ReadAlgoOrder(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    /// <inheritdoc/>
    public static AlgoOrderModel Parse(ReadOnlySpan<byte> result) => BinanceOrderReader.ReadAlgoOrder(result);
}
