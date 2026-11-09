namespace Axitor.Trading.Gateway.Core.Configuration;

/// <summary>Resiliency configuration.</summary>
public record ResiliencyOptions
{

}

/// <summary>Resiliency configuration for REST API transport.</summary>
public sealed record ResiliencyRestApiOptions : ResiliencyOptions
{
    /// <summary>Allocates a string for an exchange error message.</summary>
    public bool CaptureErrorMessageString { get; init; }

    /// <summary>Allocates a string for the raw JSON of exchange error data, e.g. both halves of a spot cancelReplace.</summary>
    public bool CaptureErrorDataString { get; init; }

    /// <summary>Allocates a string for a failure request URI.</summary>
    public bool CaptureFailureRequestUri { get; init; }

    /// <summary>Upper bound of the random delay added to every repeat after awaiting network state to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnNetworkRepaired { get; init; }

    /// <summary>Upper bound of the random delay added to every repeat after awaiting rate limits to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnRateLimits { get; init; }

    /// <summary>Upper bound of the random delay added to every retry to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnRetry { get; init; }

    /// <summary>Lower bound of the random delay added to every retry to avoid synchronized waves.</summary>
    public int MinJitterMillisOnRetry { get; init; }

    /// <summary>First delay before repeating a request.</summary>
    public int RetryBaseDelayMillis { get; init; }

    /// <summary>Longest delay between repeating a request.</summary>
    public int RetryMaxDelayMillis { get; init; }

    /// <summary>Fallback delay before repeating a request on HTTP 429 Too Many Requests without a Retry-After Header.</summary>
    public int RetryFallbackDelayMillis { get; init; }
}

/// <summary>Resiliency configuration for WebSocket routing.</summary>
public record ResiliencyWsOptions : ResiliencyOptions
{
    /// <summary>First delay before reconnecting.</summary>
    public int ReconnectBaseDelayMillis { get; init; }

    /// <summary>Longest delay between reconnect attempts.</summary>
    public int ReconnectMaxDelayMillis { get; init; }

    /// <summary>Lower bound of the random delay added to every reconnect to avoid synchronized waves.</summary>
    public int MinJitterMillisOnReconnect { get; init; }

    /// <summary>Upper bound of the random delay added to every reconnect to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnReconnect { get; init; }

    /// <summary>Upper bound of the random delay before reconnecting once the network is repaired.</summary>
    public int MaxJitterMillisOnNetworkRepaired { get; init; }

    /// <summary>Ban length when a handshake is refused with 429 and no Retry-After.</summary>
    public int ReconnectFallbackDelayMillis { get; init; }

    /// <summary>A connection that lived this long resets the reconnect back-off to its first step.</summary>
    public int ReconnectAttemptResetMillis { get; init; }
}

/// <summary>Resiliency configuration for WebSocket API command routing.</summary>
public sealed record ResiliencyWsApiOptions : ResiliencyWsOptions
{
    /// <summary>Allocates a string for an exchange error message.</summary>
    public bool CaptureErrorMessageString { get; init; }

    /// <summary>Allocates a string for the raw JSON of exchange error data, e.g. both halves of a spot cancelReplace.</summary>
    public bool CaptureErrorDataString { get; init; }

    /// <summary>Upper bound of the random delay added to every repeat after awaiting rate limits to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnRateLimits { get; init; }

    /// <summary>Upper bound of the random delay added to every retry to avoid synchronized waves.</summary>
    public int MaxJitterMillisOnRetry { get; init; }

    /// <summary>Lower bound of the random delay added to every retry to avoid synchronized waves.</summary>
    public int MinJitterMillisOnRetry { get; init; }

    /// <summary>First delay before repeating a request.</summary>
    public int RetryBaseDelayMillis { get; init; }

    /// <summary>Longest delay between repeating a request.</summary>
    public int RetryMaxDelayMillis { get; init; }

    /// <summary>Ban length when a response asks to back off (429) or bans (418) without retryAfter.</summary>
    public int RetryFallbackDelayMillis { get; init; }
}

/// <summary>Resiliency configuration for unidirectional WebSocket data streams.</summary>
public sealed record ResiliencyWsStreamOptions : ResiliencyWsOptions
{
    /// <summary>Watchdog time limit for socket inactivity before force-resconnect.</summary>
    public TimeSpan WatchdogTimeout { get; init; }
}
