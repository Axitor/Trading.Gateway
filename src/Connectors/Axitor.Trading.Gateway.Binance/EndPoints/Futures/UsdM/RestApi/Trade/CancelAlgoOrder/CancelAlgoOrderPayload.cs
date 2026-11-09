using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAlgoOrder;

/// <summary>Cancel Algo Order Endpoint PayLoad Writer. Sends the algo id, or the client algo id when it is zero.</summary>
internal readonly struct CancelAlgoOrderPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public long AlgoId { get; init; }
    public ClientOrderId ClientAlgoId { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/algoOrder"u8);

        if (0 < AlgoId)
        {
            builder.Append("algoId="u8, AlgoId);
            return;
        }

        Span<byte> id = stackalloc byte[ClientOrderId.MaxLength];
        ClientAlgoId.TryWriteTo(id, out int idLength);
        builder.Append("clientAlgoId="u8, id[..idLength]);
    }
}
