using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelOrder;

/// <summary>Cancel Order Endpoint PayLoad Writer. Sends the order id, or the client order id when it is zero.</summary>
internal readonly struct CancelOrderPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }
    public long OrderId { get; init; }
    public ClientOrderId ClientOrderId { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/order"u8);

        builder.Append("symbol="u8, Symbol);

        if (0 < OrderId)
        {
            builder.Append("orderId="u8, OrderId);
            return;
        }

        Span<byte> id = stackalloc byte[ClientOrderId.MaxLength];
        ClientOrderId.TryWriteTo(id, out int idLength);
        builder.Append("origClientOrderId="u8, id[..idLength]);
    }
}
