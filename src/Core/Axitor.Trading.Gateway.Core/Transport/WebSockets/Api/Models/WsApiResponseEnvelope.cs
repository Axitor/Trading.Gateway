namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

/// <summary>What the dialect read from a WebSocket API response.</summary>
internal struct WsApiResponseEnvelope
{
    /// <summary>Exchange error message, or null when not captured or absent.</summary>
    public string? ExchangeMessage;

    /// <summary>Raw JSON of the exchange error data, or null when not captured or absent.</summary>
    public string? ExchangeData;

    /// <summary>How long the exchange asked to wait, in milliseconds, or zero when absent.</summary>
    public long RetryAfterMillis;

    /// <summary>Where the result starts in the message.</summary>
    public int ResultStart;

    /// <summary>Length of the result in bytes, or zero when absent.</summary>
    public int ResultLength;

    /// <summary>Response status.</summary>
    public int StatusCode;

    /// <summary>Exchange error code, or zero when absent.</summary>
    public int ExchangeCode;

    /// <summary>The reason of the failure. None on success.</summary>
    public WsApiRequestErrorReason Reason;
}
