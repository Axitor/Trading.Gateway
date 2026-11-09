namespace Axitor.Trading.Gateway.Binance.DialectHelpers;

/// <summary>Failure kind from the error classifier. Each dialect maps it to its own reason.</summary>
internal enum BinanceErrorKind : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>HTTP 401.</summary>
    Unauthorized,

    /// <summary>HTTP 403 or 418. Every request extends the ban.</summary>
    BanLong,

    /// <summary>HTTP 429. Back off by Retry-After.</summary>
    BanShort,

    /// <summary>HTTP 404.</summary>
    NotFound,

    /// <summary>HTTP 413, 414 or 415.</summary>
    Malformed,

    /// <summary>HTTP 409: one half of a spot cancelReplace went through, the other did not. See the error data.</summary>
    Partial,

    /// <summary>An order with this client order id is already open.</summary>
    AlreadyPlaced,

    /// <summary>The request timestamp was rejected. Not executed.</summary>
    TimestampRejected,

    /// <summary>The request may or may not have been applied.</summary>
    StatusUnknown,

    /// <summary>The exchange says it did not process the request.</summary>
    RequestFailed,

    /// <summary>An exchange error whose action depends on its code.</summary>
    RequestError,

    /// <summary>A failure status with no rule.</summary>
    OtherError,
}
