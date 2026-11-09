using System.Net.Sockets;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

/// <summary>Result of a REST API call.</summary>
public struct RestApiRequestResult<TValue>
{
    /// <summary>Parsed response body. Meaningful only on success.</summary>
    public TValue Value;

    /// <summary>Errors of a REST API call.</summary>
    public RestApiRequestResultError Errors;

    /// <summary>Latency of a REST API call.</summary>
    public RestApiRequestResultLatency Latency;

    /// <summary>Resiliency action of a REST API response (only for errors).</summary>
    public RestApiRequestResultResiliency Resiliency;

    /// <summary>Headers of a REST API response.</summary>
    public RestApiRequestResultHeaders Headers;

    /// <summary>How the call ended (success, error, etc).</summary>
    public RequestOutcome Outcome;
}

/// <summary>Resiliency verdict of a REST API call (last error data, every failed retry will rewrite it).</summary>
public struct RestApiRequestResultResiliency
{
    /// <summary>How long to wait before retrying, in milliseconds (only for retries).</summary>
    public long RetryDelayMillis;

    /// <summary>How many times the request was retried, the number of the last one.</summary>
    public int RetryNumber;

    /// <summary>What to do in case of an error.</summary>
    public RequestErrorAction Action;

}

/// <summary>Errors of a REST API call (last error data, every failed retry will rewrite it).</summary>
public struct RestApiRequestResultError
{
    /// <summary>The full request URI, provided for request identification. Can be absent on some stages.</summary>
    public string? RequestUri;

    /// <summary>Exchange error message (if options.RestApi.Resiliency.CaptureErrorMessageString is true), or null when the exchange reported none.</summary>
    public string? ExchangeMessage;

    /// <summary>Raw JSON of the exchange error data (if options.RestApi.Resiliency.CaptureErrorDataString is true), or null when the exchange sent none.</summary>
    public string? ExchangeData;

    /// <summary>Exchange error code (always negative), or zero when the exchange reported none.</summary>
    public int ExchangeCode;

    /// <summary>HTTP status code, or zero when no response was received.</summary>
    public int StatusCode;

    /// <summary>Rule the request broke before it was sent, as the connector numbers it. Zero when none.</summary>
    public int RuleViolation;

    /// <summary>The reason of the failure.</summary>
    public RestApiRequestErrorReason Reason;

    /// <summary>HttpRequestError for TLS handshake, DNS, protocol, etc.</summary>
    public HttpRequestError HttpError;

    /// <summary>SocketError for nested-socket breaks.</summary>
    public SocketError SocketError;

    /// <summary>Indicates whether the retry loop dropped the request due to an expired TTL.</summary>
    public bool WasDroppedByTtl;

}

/// <summary>Latency of a REST API call.</summary>
public struct RestApiRequestResultLatency
{
    /// <summary>Latency of full execution (nanoseconds).</summary>
    public long ExecutionFull;

    /// <summary>Latency of resilency layers (nanoseconds).</summary>
    public long ExecutionResiliencyOnly;

    /// <summary>Latency of a full transport layer (nanoseconds).</summary>
    public long TransportFull;

    /// <summary>Latency of a network layer (nanoseconds).</summary>
    public long TransportNetworkOnly;

    /// <summary>Latency of parsing network response (nanoseconds).</summary>
    public long TransportParsingOnly;
}

/// <summary>Headers of a REST  API response.</summary>
public struct RestApiRequestResultHeaders
{
    /// <summary>Exchange header response about how long should we wait till retry the request in case of an error (can be absent).</summary>
    public int RetryAfterSeconds;

}
