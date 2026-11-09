namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

/// <summary>The reason of a REST API request failure.</summary>
public enum RestApiRequestErrorReason : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Cancellation was requested by the caller.</summary>
    CancellationByCaller,

    /// <summary>HttpClient.Timeout fired (TimeoutException).</summary>
    ExceptionTimeout,

    /// <summary>HttpRequestError for TLS handshake, DNS, protocol, etc.</summary>
    ExceptionHttp,

    /// <summary>SocketError for nested-socket breaks.</summary>
    ExceptionSocket,

    /// <summary>Response stream cut mid-body.</summary>
    ExceptionIO,

    /// <summary>
    /// A faulty test mock or a runtime glitch yields null, because
    /// the .NET SocketsHttpHandler strictly guarantees a non-null Response and its Content.
    /// </summary>
    ExceptionNull,

    /// <summary>Json parsing glitch, because exchanges strictly guarantee well-formed JSON.</summary>
    ExceptionJson,

    /// <summary>The exchange long-term ban. Every request will extend ban duration. Back off by Retry-After, read by the transport from headers.</summary>
    ExchangeApiBanLong,

    /// <summary>The exchange said to back off by Retry-After, read by the transport from headers.</summary>
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

    /// <summary>The URI is not found.</summary>
    StatusCodeNotFound,

    /// <summary>A failure status code without exact ruleset.</summary>
    StatusCodeOtherError,

    /// <summary>A spot cancelReplace partly succeeded: either the cancel or the new order went through, not both. See ExchangeData.</summary>
    ExchangeApiRequestPartial,

    /// <summary>An order with this client order id is already open: an earlier attempt reached the exchange.</summary>
    ExchangeApiOrderAlreadyPlaced,

    /// <summary>The exchange rejected the request timestamp, e.g. after a network lag. Not executed.</summary>
    ExchangeApiTimestampRejected,

    /// <summary>The order broke a symbol filter, e.g. a quantity below the minimum, and was not sent.</summary>
    OrderFiltersViolated,

}
