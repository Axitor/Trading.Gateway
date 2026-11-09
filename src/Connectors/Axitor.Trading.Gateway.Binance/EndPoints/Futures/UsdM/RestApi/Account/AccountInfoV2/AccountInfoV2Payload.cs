using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.AccountInfoV2;

/// <summary>Account Information V2 Endpoint PayLoad Writer.</summary>
internal readonly struct AccountInfoV2Payload : IPayloadWriter
{
    public byte[] Host { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v2/account"u8);
    }
}
