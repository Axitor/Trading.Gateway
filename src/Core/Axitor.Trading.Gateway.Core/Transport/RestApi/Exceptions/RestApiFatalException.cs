using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;

/// <summary>Unrecoverable gateway failure. The connector must stop; user intervention is required.</summary>
public sealed class RestApiFatalException : Exception
{
    /// <summary>The error details extracted from the failure.</summary>
    public RestApiRequestResultError Errors { get; }

    /// <summary>The resiliency context at the moment of failure.</summary>
    public RestApiRequestResultResiliency Resiliency { get; }

    /// <inheritdoc />
    public RestApiFatalException(
        in RestApiRequestResultError errors,
        in RestApiRequestResultResiliency resiliency,
        string? message = null)
        : base(message ?? "Fatal exchange or infrastructure error. The connector must stop.")
    {
        Errors = errors;
        Resiliency = resiliency;
    }
}
