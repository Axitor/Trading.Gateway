using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

/// <summary>The static half of an endpoint: everything that is the same on every call.</summary>
internal struct RestApiEndpointDescriptor
{
    /// <summary>HTTP method. A cached static instance; no allocation per call.</summary>
    public HttpMethod HttpMethod;

    /// <summary>Weight limit charged by a request.</summary>
    public int Weight;

    /// <summary>Which rate limit windows this endpoint is charged against.</summary>
    public int LimitBucket;

    /// <summary>Exchange market the endpoint belongs to. Opaque to the core, read by the dialect.</summary>
    public int Market;

    /// <summary>Time to live (milliseconds) for a request.</summary>
    public int TTL;

    /// <summary>Slot pool the call draws from.</summary>
    public RequestPriority Priority;

    /// <summary>Whether the request can be retried in case of transport failure or an exchange error.</summary>
    public bool IsRetryable;

    /// <summary>Whether the endpoint needs a timestamp.</summary>
    public bool NeedsTimestamp;

    /// <summary>Whether the endpoint needs a signature.</summary>
    public bool NeedsSignature;
}
