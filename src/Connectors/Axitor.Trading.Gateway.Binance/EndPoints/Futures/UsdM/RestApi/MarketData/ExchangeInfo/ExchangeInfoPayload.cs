using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;

/// <summary>Exchange Information Endpoint PayLoad Writer.</summary>
internal readonly struct ExchangeInfoPayload : IPayloadWriter
{
    public byte[] Host { get; init; }

    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/exchangeInfo"u8);
    }
}
