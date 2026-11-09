using Axitor.Trading.Gateway.Core.Configuration;

namespace Axitor.Trading.Gateway.Core.Engine;

/// <summary>Core engine configuration.</summary>
public sealed record EngineOptions
{
    /// <summary>REST API transport configuration.</summary>
    public RestApiTransportOptions RestApi { get; init; } = new()
    {
        Network = new NetworkRestApiOptions()
        {
            MaxConnections = 20,
            ConnectTimeout = TimeSpan.FromSeconds(5),

            RequestTimeout = TimeSpan.FromSeconds(10),
            ResponseDrainTimeout = TimeSpan.FromMilliseconds(250),
            ConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PingInterval = TimeSpan.FromSeconds(1),
        },

        Throttle = new ThrottleRestApiOptions()
        {
            NormalConcurrencySlots = 10,
            CriticalConcurrencySlots = 5,
        },

        Resiliency = new ResiliencyRestApiOptions()
        {
            CaptureErrorMessageString = false,
            CaptureErrorDataString = false,
            CaptureFailureRequestUri = false,

            MaxJitterMillisOnNetworkRepaired = 2000,
            MaxJitterMillisOnRateLimits = 2000,
            MaxJitterMillisOnRetry = 2000,
            MinJitterMillisOnRetry = 500,
            RetryBaseDelayMillis = 500,
            RetryMaxDelayMillis = 3000,
            RetryFallbackDelayMillis = 60000,
        },

        Buffer = new BufferRestApiOptions()
        {
            PayloadBufferBytes = 1024,
            ErrorBufferBytes = 16 * 1024,
        }
    };

    /// <summary>WebSocket API transport configuration.</summary>
    public WsApiTransportOptions WsApi { get; init; } = new()
    {
        Network = new NetworkWsApiOptions()
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),

            MaxInFlight = 256,
            MaxConnectionAge = TimeSpan.FromHours(23),
            KeepAliveInterval = TimeSpan.FromSeconds(5),
            KeepAliveTimeout = TimeSpan.FromSeconds(5),
            ResponseTimeout = TimeSpan.FromSeconds(5),
            TimeoutSweepInterval = TimeSpan.FromMilliseconds(250),
            MaxMessageBytes = 4 * 1024 * 1024,
            CloseTimeout = TimeSpan.FromSeconds(1),
        },

        Throttle = new ThrottleWsApiOptions()
        {

        },

        Resiliency = new ResiliencyWsApiOptions()
        {
            ReconnectBaseDelayMillis = 500,
            ReconnectMaxDelayMillis = 30000,
            MinJitterMillisOnReconnect = 250,
            MaxJitterMillisOnReconnect = 1000,
            MaxJitterMillisOnNetworkRepaired = 2000,
            ReconnectFallbackDelayMillis = 60000,
            ReconnectAttemptResetMillis = 60000,

            CaptureErrorMessageString = false,
            CaptureErrorDataString = false,
            MaxJitterMillisOnRateLimits = 2000,
            MaxJitterMillisOnRetry = 2000,
            MinJitterMillisOnRetry = 500,
            RetryBaseDelayMillis = 500,
            RetryMaxDelayMillis = 3000,
            RetryFallbackDelayMillis = 60000,
        },

        Buffer = new BufferWsApiOptions()
        {
            SendBufferBytes = 4096,
            ReceiveBufferBytes = 64 * 1024,
            PendingSlotBufferBytes = 4 * 1024,
        }
    };

    /// <summary>WebSocket Stream transport configuration.</summary>
    public WsStreamTransportOptions WsStream { get; init; } = new()
    {
        Network = new NetworkWsStreamOptions()
        {
            MaxConnections = 5,
            MaxStreamsPerConnection = 200,
            ConnectTimeout = TimeSpan.FromSeconds(5),

            KeepAliveInterval = TimeSpan.FromSeconds(5),
            KeepAliveTimeout = TimeSpan.FromSeconds(5),
            MaxMessageBytes = 1024 * 1024,
            CloseTimeout = TimeSpan.FromSeconds(1),
            MaxConnectionAge = TimeSpan.FromHours(23),
            ResponseTimeout = TimeSpan.FromSeconds(10),
        },

        Throttle = new ThrottleWsStreamOptions()
        {

        },

        Resiliency = new ResiliencyWsStreamOptions()
        {
            WatchdogTimeout = TimeSpan.FromSeconds(5),

            ReconnectBaseDelayMillis = 500,
            ReconnectMaxDelayMillis = 30000,
            MinJitterMillisOnReconnect = 250,
            MaxJitterMillisOnReconnect = 1000,
            MaxJitterMillisOnNetworkRepaired = 2000,
            ReconnectFallbackDelayMillis = 60000,
            ReconnectAttemptResetMillis = 60000,
        },

        Buffer = new BufferWsStreamOptions()
        {
            ReceiveBufferBytes = 64 * 1024,
            SendBufferBytes = 16 * 1024,
        }
    };

}
