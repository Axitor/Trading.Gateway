using System.Globalization;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Binance.DialectHelpers;

/// <summary>Classifies a refused WebSocket handshake for the WebSocket API and stream dialects.</summary>
internal static class BinanceHandshakeReader
{
    private const string RetryAfterHeader = "Retry-After";

    /// <summary>Cold path. Maps the status the way the error classifier does and reads Retry-After in seconds.</summary>
    public static void ReadHandshakeFailure(int statusCode, IReadOnlyDictionary<string, IEnumerable<string>>? headers, out WsDisconnectReason reason, out int retryAfterSeconds)
    {
        retryAfterSeconds = ReadRetryAfter(headers);

        reason = BinanceErrorClassifier.Classify(statusCode, 0, false, BinanceMarkets.None) switch
        {
            BinanceErrorKind.BanShort => WsDisconnectReason.HandshakeBanShort,
            BinanceErrorKind.BanLong => WsDisconnectReason.HandshakeBanLong,
            BinanceErrorKind.StatusUnknown => WsDisconnectReason.HandshakeUnavailable,
            _ => WsDisconnectReason.HandshakeRejected,
        };
    }

    private static int ReadRetryAfter(IReadOnlyDictionary<string, IEnumerable<string>>? headers)
    {
        if (headers is null)
        {
            return 0;
        }

        // The dictionary comparer is not guaranteed, so the name is matched ignoring case.
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            if (!string.Equals(header.Key, RetryAfterHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string value in header.Value)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && 0 < seconds ? seconds : 0;
            }

            return 0;
        }

        return 0;
    }
}
