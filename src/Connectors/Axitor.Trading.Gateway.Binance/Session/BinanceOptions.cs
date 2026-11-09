using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;

namespace Axitor.Trading.Gateway.Binance.Session;

/// <summary> Binance-specific configurartion.</summary>
public sealed record BinanceOptions
{
    /// <summary>Auth configuration.</summary>
    public BinanceSecret Secret { get; init; }

    /// <summary>URIs configuration.</summary>
    public BinanceUri Uri { get; init; } = new();

    /// <summary>Limits configuration.</summary>
    public BinanceLimits Limits { get; init; } = new();

    /// <summary>
    /// Markets whose WebSocket API connects on client init. Combine flags to enable several, e.g. FuturesUsdM | Spot.
    /// A WebSocket API request to a market left out throws InvalidOperationException.
    /// </summary>
    public BinanceMarkets WsApiMarkets { get; init; } = BinanceMarkets.FuturesUsdM;

    /// <summary>
    /// Markets whose streams can be used. Combine flags to enable several, e.g. FuturesUsdM | Spot.
    /// Creating a stream session on a market left out throws InvalidOperationException.
    /// </summary>
    public BinanceMarkets StreamMarkets { get; init; } = BinanceMarkets.FuturesUsdM;

    /// <summary>How often a user data session checks and extends its listen key. The key lives 60 minutes.</summary>
    public TimeSpan UserDataKeepAliveInterval { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Sets basic exchange-specific auth params. Null key and secret means only public endpoints are available.</summary>
    public BinanceOptions(string? key = null, string? secret = null)
    {
        Secret = new(key, secret);
    }
}
