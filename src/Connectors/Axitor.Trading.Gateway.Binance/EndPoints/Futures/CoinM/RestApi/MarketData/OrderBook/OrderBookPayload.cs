using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.RestApi.MarketData.OrderBook;

/// <summary>Order Book Endpoint PayLoad Writer.</summary>
internal readonly struct OrderBookPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }
    public int Limit { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/dapi/v1/depth"u8);

        builder.Append("symbol="u8, Symbol);
        builder.Append("limit="u8, Limit);
    }
}
