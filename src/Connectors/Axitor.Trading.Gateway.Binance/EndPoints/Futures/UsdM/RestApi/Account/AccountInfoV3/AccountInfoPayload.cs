using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.AccountInfoV3;

/// <summary>Account Information V3 Endpoint Endpoint PayLoad Writer.</summary>
internal readonly struct AccountInfoPayload : IPayloadWriter
{
    public byte[] Host { get; init; }

    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v3/account"u8);
    }
}
