namespace Axitor.Trading.Gateway.Binance.Configuration;

/// <summary> Binance-specific limits.</summary>
/// <remarks>
/// DESIGN NOTE: Rate limits are strictly configuration-driven.
/// Deliberately avoid auto-fetching limits from exchange metadata endpoints.
/// The caller is responsible for supplying the correct limit parameters for their specific account tier.
/// </remarks>
public sealed record BinanceLimits
{
    // --- Common ---

    /// <summary>Receive Window is an optional parameter used to specify the number of milliseconds a request remains valid after its creation timestamp. Sets to zero to use the exchange defaults.</summary>
    public int RecvWindowMilliseconds { get; init; }


    // --- Limits Futures REST API ---

    /// <summary>Binance Futures REST API Weight Limits, shared by USD-M and COIN-M. The maximum allowed total requests (except orders requests) weight per one minute.</summary>
    public int FuturesRestApiLimitRequestWeightPerMinute { get; init; } = 2400;

    /// <summary>Binance Futures REST API Weight Limits, shared by USD-M and COIN-M. The maximum allowed total orders requests weight per one minute.</summary>
    public int FuturesRestApiLimitOrderWeightPerMinute { get; init; } = 1200;

    /// <summary>Binance Futures REST API Weight Limits, shared by USD-M and COIN-M. The maximum allowed total orders requests weight per 10 second.</summary>
    public int FuturesRestApiLimitOrderWeightPer10s { get; init; } = 300;

    /// <summary>
    /// Percent of the futures request weight per minute that order book snapshots may take, so resyncs never hold back orders and cancels.
    /// Shared by every order book of the client and by direct snapshot calls.
    /// </summary>
    public int FuturesDepthSnapshotWeightPercent { get; init; } = 50;


    // --- Limits Spot REST API ---

    /// <summary>Binance Spot REST API Limits. The maximum allowed total requests (except orders requests) weight per one minute.</summary>
    public int SpotRestApiLimitRequestWeightPerMinute { get; init; } = 6000;

    /// <summary>Binance Spot REST API Limits. The maximum allowed orders amount per 10 seconds.</summary>
    public int SpotRestApiLimitOrderAmountPer10s { get; init; } = 100;

    /// <summary>Binance Spot REST API Limits. The maximum allowed orders amount per 24 hours.</summary>
    public int SpotRestApiLimitOrderAmountPer24h { get; init; } = 200000;

    /// <summary>
    /// Percent of the spot request weight per minute that order book snapshots may take, so resyncs never hold back orders and cancels.
    /// Shared by every order book of the client and by direct snapshot calls.
    /// </summary>
    public int SpotDepthSnapshotWeightPercent { get; init; } = 50;


    // --- Limits Futures WebSocket API ---

    /// <summary>Binance USD-M Futures WebSocket API Weight Limits. The maximum allowed total requests weight per one minute. COIN-M is charged to the REST API window.</summary>
    public int FuturesWsApiLimitRequestWeightPerMinute { get; init; } = 2400;

    /// <summary>Charges USD-M Futures WebSocket API weight to the REST API weight window instead of its own.</summary>
    public bool FuturesWsApiSharesRestRequestWeight { get; init; } = true;


    // --- Limits Spot WebSocket API ---

    /// <summary>Binance Spot WebSocket API Weight Limits. The maximum allowed total requests weight per one minute.</summary>
    public int SpotWsApiLimitRequestWeightPerMinute { get; init; } = 6000;

    /// <summary>Charges Spot WebSocket API weight to the REST API weight window instead of its own.</summary>
    public bool SpotWsApiSharesRestRequestWeight { get; init; } = true;


    // --- Limits Futures Websocket Stream ---

    /// <summary>Binance Futures Websocket Stream Limits. The maximum allowed total messages amount per second.</summary>
    public int FuturesWebsocketStreamLimitMessageAmountPerSecond { get; init; } = 10;

    /// <summary>Binance Futures Websocket Stream Limits. A limit of connections per attempt every 5 minutes per IP.</summary>
    public int FuturesWebsocketStreamLimitConnectionsAmountPerAttemptInFiveMinutes { get; init; } = 300;


    // --- Limits Spot Websocket Stream ---

    /// <summary>Binance Spot Websocket Stream Limits. The maximum allowed total messages amount per second.</summary>
    public int SpotWebsocketStreamLimitMessageAmountPerSecond { get; init; } = 5;

    /// <summary>Binance Spot Websocket Stream Limits. A limit of connections per attempt every 5 minutes per IP.</summary>
    public int SpotWebsocketStreamLimitConnectionsAmountPerAttemptInFiveMinutes { get; init; } = 300;
}
