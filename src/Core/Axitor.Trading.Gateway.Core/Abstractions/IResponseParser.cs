namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Turns a response body into the endpoint's value. Implement as a readonly struct; a parser with no state is passed as default.</summary>
internal interface IResponseParser<TValue>
{
    /// <summary>Reads the body. The stream is owned by the caller and disposed after this returns.</summary>
    ValueTask<TValue> ParseAsync(Stream body, CancellationToken cancellationToken);
}
