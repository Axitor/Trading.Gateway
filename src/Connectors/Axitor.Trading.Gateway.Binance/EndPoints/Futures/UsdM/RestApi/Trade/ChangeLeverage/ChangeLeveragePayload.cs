using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;

/// <summary>Change Initial Leverage Endpoint PayLoad Writer.</summary>
internal readonly struct ChangeLeveragePayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }
    public int Leverage { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/leverage"u8);

        builder.Append("symbol="u8, Symbol);
        builder.Append("leverage="u8, Leverage);
    }
}
