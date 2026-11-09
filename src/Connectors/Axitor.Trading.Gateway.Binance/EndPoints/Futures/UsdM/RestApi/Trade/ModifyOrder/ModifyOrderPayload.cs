using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ModifyOrder;

/// <summary>Modify Order Endpoint PayLoad Writer. Sends the order id, or the client order id when it is zero.</summary>
internal readonly struct ModifyOrderPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }
    public long OrderId { get; init; }
    public ClientOrderId ClientOrderId { get; init; }
    public double Quantity { get; init; }
    public double Price { get; init; }
    public long ModifyId { get; init; }
    public OrderSide Side { get; init; }
    public bool IsReduceOnly { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/order"u8);

        builder.Append("symbol="u8, Symbol);

        if (0 < OrderId)
        {
            builder.Append("orderId="u8, OrderId);
        }
        else
        {
            Span<byte> id = stackalloc byte[ClientOrderId.MaxLength];
            ClientOrderId.TryWriteTo(id, out int idLength);
            builder.Append("origClientOrderId="u8, id[..idLength]);
        }

        builder.Append("side="u8, Side.ToUtf8Bytes());
        builder.AppendDecimal("quantity="u8, Quantity);
        builder.AppendDecimal("price="u8, Price);

        // Only a check on the exchange; false is its default.
        if (IsReduceOnly)
        {
            builder.Append("reduceOnly="u8, true);
        }

        if (ModifyId != 0)
        {
            builder.Append("modifyId="u8, ModifyId);
        }
    }
}
