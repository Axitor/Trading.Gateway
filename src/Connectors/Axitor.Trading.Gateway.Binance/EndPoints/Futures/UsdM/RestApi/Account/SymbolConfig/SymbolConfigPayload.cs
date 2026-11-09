using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;

/// <summary>Symbol Configuration Endpoint PayLoad Writer. An empty symbol asks for every symbol.</summary>
internal readonly struct SymbolConfigPayload : IPayloadWriter
{
    public byte[] Host { get; init; }
    public Symbol Symbol { get; init; }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/symbolConfig"u8);

        if (!Symbol.IsEmpty)
        {
            builder.Append("symbol="u8, Symbol);
        }
    }
}
