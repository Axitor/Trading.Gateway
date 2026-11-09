using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAllOpenOrders;

/// <summary>Cancel All Open Orders Endpoint PayLoad Writer.</summary>
internal readonly struct CancelAllOpenOrdersPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/allOpenOrders"u8);

        builder.Append("symbol="u8, Symbol);
    }
}
