using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Orders;

/// <summary>Order response parser of REST and the WebSocket API: place, cancel, modify and test.</summary>
internal readonly struct OrderParser : IResponseParser<OrderModel>, IWsApiResultParser<OrderModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<OrderModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return BinanceOrderReader.ReadOrder(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    /// <inheritdoc/>
    public static OrderModel Parse(ReadOnlySpan<byte> result) => BinanceOrderReader.ReadOrder(result);
}
