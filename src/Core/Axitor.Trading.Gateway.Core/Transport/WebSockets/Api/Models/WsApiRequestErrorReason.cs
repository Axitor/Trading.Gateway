namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>The reason of a WebSocket API request failure.</summary>
public enum WsApiRequestErrorReason : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Cancellation was requested by the caller.</summary>
    CancellationByCaller,

    /// <summary>Never reached the wire. Not executed.</summary>
    NotSent,

    /// <summary>No open connection to send on. Not executed.</summary>
    NoConnection,

    /// <summary>Every in-flight slot was busy. Not executed.</summary>
    InFlightLimit,

    /// <summary>No response within the response timeout. The request may have been applied.</summary>
    Timeout,

    /// <summary>The connection broke before the response. The request may have been applied.</summary>
    ConnectionLost,

    /// <summary>The connection failed in a way retrying cannot fix. See Disconnect.</summary>
    ConnectionFatal,

    /// <summary>A response with our id arrived but could not be read. The request may have been applied.</summary>
    ExceptionJson,

    /// <summary>The exchange long-term ban. Every request will extend ban duration.</summary>
    ExchangeApiBanLong,

    /// <summary>The exchange said to back off by retryAfter.</summary>
    ExchangeApiBanShort,

    /// <summary>The exchange responded that the request was unauthorized.</summary>
    ExchangeApiUnauthorized,

    /// <summary>The exchange does not know whether the request was applied: 408, any 5xx, or -1000, -1006, -1007.</summary>
    ExchangeApiRequestStatusUnknown,

    /// <summary>The request is malformed. The server rejected it.</summary>
    ExchangeApiRequestMalformed,

    /// <summary>The exchange returned an API error, check an exchange error code and a message.</summary>
    ExchangeApiRequestError,

    /// <summary>The exchange says it did not process the request (-1001, -1008). Safe to retry.</summary>
    ExchangeApiRequestFailed,

    /// <summary>A spot cancelReplace partly succeeded: either the cancel or the new order went through, not both. See ExchangeData.</summary>
    ExchangeApiRequestPartial,

    /// <summary>An order with this client order id is already open: an earlier attempt reached the exchange.</summary>
    ExchangeApiOrderAlreadyPlaced,

    /// <summary>The exchange rejected the request timestamp, e.g. after a network lag. Not executed.</summary>
    ExchangeApiTimestampRejected,

    /// <summary>The method is not found.</summary>
    StatusCodeNotFound,

    /// <summary>A failure status code without exact ruleset.</summary>
    StatusCodeOtherError,

    /// <summary>The order broke a symbol filter, e.g. a quantity below the minimum, and was not sent.</summary>
    OrderFiltersViolated,
}
