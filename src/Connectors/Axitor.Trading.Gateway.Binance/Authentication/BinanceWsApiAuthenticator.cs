using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.Authentication;

/// <summary>Signs a WebSocket API request with HMAC-SHA256 and adds apiKey, recvWindow and timestamp to its params.</summary>
internal readonly struct BinanceWsApiAuthenticator : IWsApiAuthenticator
{
    private readonly byte[] _apiKey;
    private readonly byte[] _secret;
    private readonly int _recvWindowMillis;

    /// <summary>Creates an authenticator. Zero recvWindow uses the exchange default.</summary>
    internal BinanceWsApiAuthenticator(byte[] apiKey, byte[] secret, int recvWindowMillis = 0)
    {
        _apiKey = apiKey;
        _secret = secret;
        _recvWindowMillis = recvWindowMillis;
    }

    /// <inheritdoc />
    public static bool Signs => true;

    /// <inheritdoc />
    public void AddParams(ref WsApiRequestBuilder builder)
    {
        // Queued in name order. Runs under the send lock, so the timestamp is taken right before the frame goes out.
        builder.AddAuthText("apiKey"u8, _apiKey);

        if (0 < _recvWindowMillis)
        {
            builder.AddAuthInteger("recvWindow"u8, _recvWindowMillis);
        }

        builder.AddAuthInteger("timestamp"u8, SysTime.UtcMillis());
    }

    /// <inheritdoc />
    public void AppendSignature(ref WsApiRequestBuilder builder)
    {
        Span<byte> hex = stackalloc byte[BinanceSignature.HexLength];
        BinanceSignature.WriteHex(_secret, builder.Query, hex);

        builder.AppendSignature("signature"u8, hex);
    }
}
