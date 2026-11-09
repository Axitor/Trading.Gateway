using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Adds auth params to a WebSocket API request and signs it. Implement as a readonly struct.</summary>
internal interface IWsApiAuthenticator
{
    /// <summary>Whether requests are signed, so the builder keeps the query form for the signature.</summary>
    static abstract bool Signs { get; }

    /// <summary>Adds the auth params in ordinal name order; the builder merges them into the request params.</summary>
    void AddParams(ref WsApiRequestBuilder builder);

    /// <summary>Signs the query form and appends the signature.</summary>
    void AppendSignature(ref WsApiRequestBuilder builder);
}
