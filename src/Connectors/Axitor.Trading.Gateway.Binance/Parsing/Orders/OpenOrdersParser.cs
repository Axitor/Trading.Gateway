using System.Buffers;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Orders;

/// <summary>Open orders response parser.</summary>
internal readonly struct OpenOrdersParser : IResponseParser<OpenOrdersModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<OpenOrdersModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken).ConfigureAwait(false);

        try
        {
            return new OpenOrdersModel() { Orders = BinanceOrderReader.ReadOrders(read.Buffer.AsSpan(0, read.Length)) };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }
}
