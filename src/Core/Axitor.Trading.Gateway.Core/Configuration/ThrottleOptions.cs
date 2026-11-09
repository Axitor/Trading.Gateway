namespace Axitor.Trading.Gateway.Core.Configuration;

/// <summary>Throttle configuration.</summary>
public record ThrottleOptions
{
}

/// <summary>Throttle configuration for REST API transport.</summary>
public sealed record ThrottleRestApiOptions : ThrottleOptions
{
    /// <summary>Concurrent calls allowed at ordinary priority.</summary>
    public int NormalConcurrencySlots { get; init; }

    /// <summary>Concurrent calls allowed at critical priority.</summary>
    public int CriticalConcurrencySlots { get; init; }

}

/// <summary>Throttle configuration for WebSocket API command routing.</summary>
public sealed record ThrottleWsApiOptions : ThrottleOptions
{
}

/// <summary>Throttle configuration for unidirectional WebSocket data streams.</summary>
public sealed record ThrottleWsStreamOptions : ThrottleOptions
{
}
