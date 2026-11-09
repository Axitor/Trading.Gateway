namespace Axitor.Trading.Gateway.Core.Configuration;

/// <summary>Base network configuration shared across transports.</summary>
public record NetworkOptions
{
    /// <summary>Maximum time allowed to establish the initial connection.</summary>
    public TimeSpan ConnectTimeout { get; init; }
}

/// <summary>Network configuration for REST API transport.</summary>
public sealed record NetworkRestApiOptions : NetworkOptions
{
    /// <summary>Maximum parallel connections maintained in the pool.</summary>
    public int MaxConnections { get; init; }

    /// <summary>Maximum duration to wait for a response before abandoning the request.</summary>
    public TimeSpan RequestTimeout { get; init; }

    /// <summary>Time allowed to drain an unread response body before discarding the connection.</summary>
    public TimeSpan ResponseDrainTimeout { get; init; }

    /// <summary>Maximum lifetime of a pooled connection before it is recycled for DNS freshness.</summary>
    public TimeSpan ConnectionLifetime { get; init; }

    /// <summary>Idle duration after which an unused pooled connection is closed.</summary>
    public TimeSpan ConnectionIdleTimeout { get; init; }

    /// <summary>Exchange pings interval.</summary>
    public TimeSpan PingInterval { get; init; }
}

/// <summary>Network configuration for WebSocket API command routing.</summary>
public sealed record NetworkWsApiOptions : NetworkOptions
{
    /// <summary>Maximum concurrent requests in flight awaiting a response. Must be a power of two.</summary>
    public int MaxInFlight { get; init; }

    /// <summary>Maximum age of a single WebSocket session before an intentional reconnect.</summary>
    public TimeSpan MaxConnectionAge { get; init; }

    /// <summary>Interval between background keep-alive ping frames sent to the server.</summary>
    public TimeSpan KeepAliveInterval { get; init; }

    /// <summary>Maximum time to wait for a keep-alive pong response before dropping the link.</summary>
    public TimeSpan KeepAliveTimeout { get; init; }

    /// <summary>Maximum time to wait for an explicit command acknowledgment from the server.</summary>
    public TimeSpan ResponseTimeout { get; init; }

    /// <summary>Frequency at which the correlation table is swept for timed-out requests.</summary>
    public TimeSpan TimeoutSweepInterval { get; init; }

    /// <summary>Largest incoming message. A bigger one closes the connection as MessageTooBig; requests in flight end as unknown.</summary>
    public int MaxMessageBytes { get; init; }

    /// <summary>Time allowed to send a close frame before the connection is aborted.</summary>
    public TimeSpan CloseTimeout { get; init; }
}

/// <summary>Network configuration for unidirectional WebSocket data streams.</summary>
public sealed record NetworkWsStreamOptions : NetworkOptions
{
    /// <summary>Most connections per route. More connections carry more streams without adding latency.</summary>
    public int MaxConnections { get; init; }

    /// <summary>Most streams on one connection. A connection handles its streams in turn, so a higher value adds latency on busy streams.</summary>
    public int MaxStreamsPerConnection { get; init; }

    /// <summary>Interval between background keep-alive ping frames sent to the server.</summary>
    public TimeSpan KeepAliveInterval { get; init; }

    /// <summary>Maximum time to wait for a keep-alive pong response before dropping the link.</summary>
    public TimeSpan KeepAliveTimeout { get; init; }

    /// <summary>Largest incoming message. A bigger one closes the connection as MessageTooBig; its streams get a gap and reconnect.</summary>
    public int MaxMessageBytes { get; init; }

    /// <summary>Time allowed to send a close frame before the connection is aborted.</summary>
    public TimeSpan CloseTimeout { get; init; }

    /// <summary>Age at which a connection is replaced by a new one, before the exchange drops it.</summary>
    public TimeSpan MaxConnectionAge { get; init; }

    /// <summary>Maximum time to wait for the reply to a subscribe or unsubscribe request.</summary>
    public TimeSpan ResponseTimeout { get; init; }
}
