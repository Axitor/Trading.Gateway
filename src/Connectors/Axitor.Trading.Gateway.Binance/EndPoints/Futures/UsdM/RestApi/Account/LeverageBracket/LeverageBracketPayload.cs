using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;

/// <summary>Notional and Leverage Brackets Endpoint PayLoad Writer. An empty symbol asks for every symbol.</summary>
internal readonly struct LeverageBracketPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/leverageBracket"u8);

        if (!Symbol.IsEmpty)
        {
            builder.Append("symbol="u8, Symbol);
        }
    }
}
