using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>Result of a WebSocket API call.</summary>
public struct WsApiRequestResult<TValue>
{
    /// <summary>Parsed response result. Meaningful only on success.</summary>
    public TValue Value;

    /// <summary>Id the last attempt was sent with, or zero when it never got one.</summary>
    public long RequestId;

    /// <summary>Errors of a WebSocket API call.</summary>
    public WsApiRequestResultError Errors;

    /// <summary>Latency of a WebSocket API call.</summary>
    public WsApiRequestResultLatency Latency;

    /// <summary>Resiliency action of a WebSocket API call (only for errors).</summary>
    public WsApiRequestResultResiliency Resiliency;

    /// <summary>How the call ended (success, error, etc).</summary>
    public RequestOutcome Outcome;
}

/// <summary>Resiliency verdict of a WebSocket API call (last error data, every failed retry will rewrite it).</summary>
public struct WsApiRequestResultResiliency
{
    /// <summary>How long to wait before retrying, in milliseconds (only for retries).</summary>
    public long RetryDelayMillis;

    /// <summary>How many times the request was retried, the number of the last one.</summary>
    public int RetryNumber;

    /// <summary>What to do in case of an error.</summary>
    public RequestErrorAction Action;
}

/// <summary>Errors of a WebSocket API call (last error data, every failed retry will rewrite it).</summary>
public struct WsApiRequestResultError
{
    /// <summary>Exchange error message (if CaptureErrorMessageString is true), or null when the exchange reported none.</summary>
    public string? ExchangeMessage;

    /// <summary>Raw JSON of the exchange error data (if CaptureErrorDataString is true), or null when the exchange sent none.</summary>
    public string? ExchangeData;

    /// <summary>Why the connection ended, for failures at the connection level.</summary>
    public WsDisconnectInfo Disconnect;

    /// <summary>Id the last attempt was sent with, or zero when it never got one.</summary>
    public long RequestId;

    /// <summary>How long the exchange asked to wait, in milliseconds, or zero when absent.</summary>
    public long RetryAfterMillis;

    /// <summary>Exchange error code (always negative), or zero when the exchange reported none.</summary>
    public int ExchangeCode;

    /// <summary>Status from the response, or zero when no response was received.</summary>
    public int StatusCode;

    /// <summary>Rule the request broke before it was sent, as the connector numbers it. Zero when none.</summary>
    public int RuleViolation;

    /// <summary>The reason of the failure.</summary>
    public WsApiRequestErrorReason Reason;

    /// <summary>Indicates whether the retry loop dropped the request due to an expired TTL.</summary>
    public bool WasDroppedByTtl;
}

/// <summary>Latency of a WebSocket API call.</summary>
public struct WsApiRequestResultLatency
{
    /// <summary>Latency of full execution (nanoseconds).</summary>
    public long ExecutionFull;

    /// <summary>Latency of resiliency layers (nanoseconds).</summary>
    public long ExecutionResiliencyOnly;

    /// <summary>Latency of a full transport layer (nanoseconds).</summary>
    public long TransportFull;

    /// <summary>From the frame on the wire to the response arriving (nanoseconds).</summary>
    public long TransportNetworkOnly;

    /// <summary>Latency of parsing the response (nanoseconds).</summary>
    public long TransportParsingOnly;
}
