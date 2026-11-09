using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.CoinM.RestApi.MarketData.ServerTime;

/// <summary>Server Time Endpoint PayLoad Writer.</summary>
internal readonly struct ServerTimePayload : IPayloadWriter
{
    public byte[] Host { get; init; }

    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/dapi/v1/time"u8);
    }
}
