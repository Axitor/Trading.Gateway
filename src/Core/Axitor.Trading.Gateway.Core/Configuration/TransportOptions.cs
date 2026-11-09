namespace Axitor.Trading.Gateway.Core.Configuration;

/// <summary>Base generic transport configuration.</summary>
public abstract record TransportOptions<TNetwork, TThrottle, TResiliency>
    where TNetwork : new()
    where TThrottle : new()
    where TResiliency : new()
{
    /// <summary>Network configuration.</summary>
    public TNetwork Network { get; init; } = new();

    /// <summary>Throttle configuration.</summary>
    public TThrottle Throttle { get; init; } = new();

    /// <summary>Resiliency configuration.</summary>
    public TResiliency Resiliency { get; init; } = new();
}

/// <summary>REST API transport configuration.</summary>
public sealed record RestApiTransportOptions : TransportOptions<NetworkRestApiOptions, ThrottleRestApiOptions, ResiliencyRestApiOptions>
{
    internal BufferRestApiOptions Buffer { get; init; } = new();
}

/// <summary>WebSocket API transport configuration.</summary>
public sealed record WsApiTransportOptions : TransportOptions<NetworkWsApiOptions, ThrottleWsApiOptions, ResiliencyWsApiOptions>
{
    internal BufferWsApiOptions Buffer { get; init; } = new();
}

/// <summary>WebSocket Stream transport configuration.</summary>
public sealed record WsStreamTransportOptions : TransportOptions<NetworkWsStreamOptions, ThrottleWsStreamOptions, ResiliencyWsStreamOptions>
{
    internal BufferWsStreamOptions Buffer { get; init; } = new();
}
