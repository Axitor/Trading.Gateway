using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;

/// <summary>Unrecoverable WebSocket API failure. The connector must stop; user intervention is required.</summary>
public sealed class WsApiFatalException : Exception
{
    /// <summary>The error details extracted from the failure. Connection-level causes are in Errors.Disconnect.</summary>
    public WsApiRequestResultError Errors { get; }

    /// <summary>The resiliency context at the moment of failure.</summary>
    public WsApiRequestResultResiliency Resiliency { get; }

    /// <inheritdoc />
    public WsApiFatalException(in WsApiRequestResultError errors, in WsApiRequestResultResiliency resiliency, string? message = null)
        : base(message ?? "Fatal exchange or infrastructure error on the WebSocket API. The connector must stop.", errors.Disconnect.Fault)
    {
        Errors = errors;
        Resiliency = resiliency;
    }
}
