using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.Authentication;

/// <summary>Binance Authentication - HMAC-SHA256 signing, plus the optional recvWindow and required timestamp a signed call carries.</summary>
/// <remarks>
/// DESIGN NOTE: No pre-flight credential validation on the hot path.
/// As explicitly stated in the client constructor documentation, options are applied without validation.
/// Calling a signed endpoint with a public-only client (null keys) will naturally fail-fast at the exchange level.
/// </remarks>
internal readonly struct BinanceRestApiAuthenticator : IRequestAuthenticator
{
    private readonly byte[] _secret;
    private readonly int _recvWindowMillis;

    /// <summary>
    /// Creates an authenticator.
    /// </summary>
    internal BinanceRestApiAuthenticator(byte[] secret, int recvWindowMillis = 0)
    {
        _secret = secret;
        _recvWindowMillis = recvWindowMillis;
    }

    /// <inheritdoc />
    public void AppendTimestamp(ref PayloadBuilder builder, long timestampMillis)
    {
        if (_recvWindowMillis > 0)
        {
            builder.Append("recvWindow="u8, _recvWindowMillis);
        }

        builder.Append("timestamp="u8, timestampMillis);
    }

    /// <inheritdoc />
    public void AppendSignature(ref PayloadBuilder builder)
    {
        Span<byte> hex = stackalloc byte[BinanceSignature.HexLength];
        BinanceSignature.WriteHex(_secret, builder.QuerySpan(), hex);

        builder.Append("signature="u8, hex);
    }
}
