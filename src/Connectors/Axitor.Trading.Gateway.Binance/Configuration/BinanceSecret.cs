using System.Text;

namespace Axitor.Trading.Gateway.Binance.Configuration;

/// <summary>
/// Binance-specific auth params.
/// Note: A class instead of a record - for protection against ToString().
/// </summary>
public sealed class BinanceSecret
{
    /// <summary>API Key Header for the exchange. Used for signed endpoints.</summary>
    public string ApiKeyHeader { get; init; } = "X-MBX-APIKEY";

    /// <summary>API Key for the exchange. Used for signed endpoints.</summary>
    public string? ApiKey { get; init; }

    /// <summary>API Secret for HMAC-SHA256 signature generation.</summary>
    public string? ApiSecret { get; init; }

    /// <summary>Ed25519 API key for the spot SBE streams, sent in the handshake header. Only the key: the streams need no signature.</summary>
    public string? Ed25519ApiKey { get; init; }

    /// <summary>API Secret as UTF-8 bytes, converted once. Empty when no credentials were supplied.</summary>
    internal byte[] GetSecretBytes() => ApiSecret is null ? [] : Encoding.UTF8.GetBytes(ApiSecret);

    /// <summary>API Key as UTF-8 bytes, converted once. Empty when no credentials were supplied.</summary>
    internal byte[] GetApiKeyBytes() => ApiKey is null ? [] : Encoding.UTF8.GetBytes(ApiKey);

    /// <summary>Sets basic exchange-specific auth params. Null key and secret means only public endpoints are available.</summary>
    public BinanceSecret(string? key = null, string? secret = null)
    {
        ApiKey = key;
        ApiSecret = secret;
    }

}
