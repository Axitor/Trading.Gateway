using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>The static half of a WebSocket API endpoint: everything that is the same on every call.</summary>
internal struct WsApiEndpointDescriptor
{
    /// <summary>Method name for telemetry, built once when the endpoint is created.</summary>
    public string Method;

    /// <summary>Weight limit charged by a request.</summary>
    public int Weight;

    /// <summary>Which rate limit windows this endpoint is charged against.</summary>
    public int LimitBucket;

    /// <summary>Exchange market the endpoint belongs to. Opaque to the core, read by the dialect.</summary>
    public int Market;

    /// <summary>Time to live (milliseconds) for a request.</summary>
    public int TTL;

    /// <summary>Lane the request goes to.</summary>
    public RequestPriority Priority;

    /// <summary>Whether the request can be retried in case of transport failure or an exchange error.</summary>
    public bool IsRetryable;
}
