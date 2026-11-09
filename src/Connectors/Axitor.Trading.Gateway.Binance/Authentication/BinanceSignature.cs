using System.Security.Cryptography;

namespace Axitor.Trading.Gateway.Binance.Authentication;

/// <summary>HMAC-SHA256 signature in lower-case hex, as the exchange expects it on REST and the WebSocket API.</summary>
internal static class BinanceSignature
{
    private static ReadOnlySpan<byte> HexDigits => "0123456789abcdef"u8;

    /// <summary>Length of the signature in hex characters.</summary>
    public const int HexLength = HMACSHA256.HashSizeInBytes * 2;

    /// <summary>Signs the data and writes the signature as hex. The destination must hold HexLength bytes.</summary>
    public static void WriteHex(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> data, Span<byte> destination)
    {
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(secret, data, hash);

        for (int i = 0; i < hash.Length; i++)
        {
            byte value = hash[i];
            destination[i * 2] = HexDigits[value >> 4];
            destination[i * 2 + 1] = HexDigits[value & 0x0F];
        }
    }
}
