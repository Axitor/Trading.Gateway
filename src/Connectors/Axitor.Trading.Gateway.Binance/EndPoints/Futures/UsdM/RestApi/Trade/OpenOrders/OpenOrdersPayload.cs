using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.OpenOrders;

/// <summary>Current All Open Orders Endpoint PayLoad Writer. An empty symbol asks for every symbol.</summary>
internal readonly struct OpenOrdersPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/openOrders"u8);

        if (!Symbol.IsEmpty)
        {
            builder.Append("symbol="u8, Symbol);
        }
    }
}
