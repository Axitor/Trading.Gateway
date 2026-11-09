using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Params of one WebSocket API endpoint. Implement as a readonly struct.</summary>
internal interface IWsApiRequestWriter
{
    /// <summary>Method name, e.g. "order.place".</summary>
    static abstract ReadOnlySpan<byte> Method { get; }

    /// <summary>Writes the params in ordinal name order, uppercase before lowercase: the signature depends on it.</summary>
    void Write(ref WsApiRequestBuilder builder);
}
