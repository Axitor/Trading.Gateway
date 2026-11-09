using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.CancelAlgoOrder;

/// <summary>Cancel Algo Order Endpoint PayLoad Writer. Sends the algo id, or the client algo id when it is zero.</summary>
internal readonly struct CancelAlgoOrderPayload : IWsApiRequestWriter
{
    public long AlgoId { get; init; }
    public ClientOrderId ClientAlgoId { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "algoOrder.cancel"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder)
    {
        if (0 < AlgoId)
        {
            builder.AppendInteger("algoId"u8, AlgoId);
            return;
        }

        ClientOrderId clientAlgoId = ClientAlgoId;
        builder.AppendClientOrderId("clientAlgoId"u8, in clientAlgoId);
    }
}
