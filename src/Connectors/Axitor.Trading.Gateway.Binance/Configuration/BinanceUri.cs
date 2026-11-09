using System.Text;

namespace Axitor.Trading.Gateway.Binance.Configuration;

/// <summary> Binance-specific URIs.</summary>
public sealed record BinanceUri
{
    // --- Common ---

    /// <summary>Ping URI.</summary>
    public Uri PingURI { get; init; } = new("https://fapi.binance.com/fapi/v1/time");

    /// <summary>Stream on the USD-M public route the watchdog listens to. Must never go quiet, e.g. a major pair's book ticker.</summary>
    public byte[] WatchdogStream { get; init; } = Encoding.UTF8.GetBytes("btcusdt@bookTicker");

    // --- USD-M futures ---

    /// <summary>USD-M futures REST host.</summary>
    public byte[] FuturesUsdRestApi { get; init; } = Encoding.UTF8.GetBytes("https://fapi.binance.com");

    /// <summary>USD-M futures WebSocket API host.</summary>
    public Uri FuturesUsdWebSocketApi { get; init; } = new("wss://ws-fapi.binance.com/ws-fapi/v1");

    /// <summary>USD-M futures stream route for high-frequency public data: book tickers and depth.</summary>
    public Uri FuturesUsdWebSocketStreamPublic { get; init; } = new("wss://fstream.binance.com/public/stream");

    /// <summary>USD-M futures stream route for regular market data: trades, mark price, klines, tickers.</summary>
    public Uri FuturesUsdWebSocketStreamMarket { get; init; } = new("wss://fstream.binance.com/market/stream");

    /// <summary>USD-M futures stream route for user data.</summary>
    public Uri FuturesUsdWebSocketStreamPrivate { get; init; } = new("wss://fstream.binance.com/private/stream");

    // --- COIN-M futures ---

    /// <summary>COIN-M futures REST host.</summary>
    public byte[] FuturesCoinRestApi { get; init; } = Encoding.UTF8.GetBytes("https://dapi.binance.com");

    /// <summary>COIN-M futures WebSocket API host.</summary>
    public Uri FuturesCoinWebSocketApi { get; init; } = new("wss://ws-dapi.binance.com/ws-dapi/v1");

    /// <summary>COIN-M futures stream route. COIN-M does not split streams by route, so every stream goes here.</summary>
    public Uri FuturesCoinWebSocketStream { get; init; } = new("wss://dstream.binance.com/stream");

    // --- Spot ---

    /// <summary>Spot REST host.</summary>
    public byte[] SpotRestApi { get; init; } = Encoding.UTF8.GetBytes("https://api.binance.com");

    /// <summary>Spot WebSocket API host.</summary>
    public Uri SpotWebSocketApi { get; init; } = new("wss://ws-api.binance.com/ws-api/v3");

    /// <summary>Spot stream route. Spot does not split streams by route, so every stream goes here.</summary>
    public Uri SpotWebSocketStream { get; init; } = new("wss://stream.binance.com:9443/stream");

    /// <summary>Spot SBE stream route. Every SBE stream goes here.</summary>
    public Uri SpotSbeStream { get; init; } = new("wss://stream-sbe.binance.com:9443/stream");

}
