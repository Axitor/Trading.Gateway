namespace Axitor.Trading.Gateway.Binance.RateLimiter;

/// <summary>Rate limit windows a call is charged against.</summary>
[Flags]
internal enum BinanceRateLimitBucket : ushort
{
    /// <summary>No limit applied.</summary>
    None = 0,

    /// <summary>Futures request weight, one minute window. Shared by USD-M and COIN-M.</summary>
    FuturesRequestWeight = 1 << 0,

    /// <summary>Futures order weight, one minute window. Shared by USD-M and COIN-M.</summary>
    FuturesOrderMinute = 1 << 1,

    /// <summary>Futures order weight, ten second window. Shared by USD-M and COIN-M.</summary>
    FuturesOrderTenSeconds = 1 << 2,

    /// <summary>Both futures order windows. What a single futures order endpoint declares.</summary>
    FuturesOrder = FuturesOrderMinute | FuturesOrderTenSeconds,

    /// <summary>Spot request weight, one minute window.</summary>
    SpotRequestWeight = 1 << 3,

    /// <summary>Spot order weight, ten second window.</summary>
    SpotOrderTenSeconds = 1 << 4,

    /// <summary>Spot order weight, one day window.</summary>
    SpotOrderDay = 1 << 5,

    /// <summary>Both spot order windows. What a single spot order endpoint declares.</summary>
    SpotOrder = SpotOrderTenSeconds | SpotOrderDay,

    /// <summary>USD-M WebSocket API request weight, one minute window. The REST window while the limits are shared. COIN-M charges the REST window directly.</summary>
    FuturesWsApiRequestWeight = 1 << 6,

    /// <summary>Spot WebSocket API request weight, one minute window. The REST window while the limits are shared.</summary>
    SpotWsApiRequestWeight = 1 << 7,

    /// <summary>Futures stream connection attempts, five minute window. Shared by USD-M and COIN-M. Counts attempts, not weight.</summary>
    FuturesStreamConnections = 1 << 8,

    /// <summary>Spot stream connection attempts, five minute window. Counts attempts, not weight.</summary>
    SpotStreamConnections = 1 << 9,

    /// <summary>Share of the futures request weight order book snapshots may take, one minute window. Declared together with FuturesRequestWeight.</summary>
    FuturesDepthSnapshotWeight = 1 << 10,

    /// <summary>Share of the spot request weight order book snapshots may take, one minute window. Declared together with SpotRequestWeight.</summary>
    SpotDepthSnapshotWeight = 1 << 11,
}
