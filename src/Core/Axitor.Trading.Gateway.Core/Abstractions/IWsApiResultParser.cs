namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Turns the result of a WebSocket API response into the endpoint's value. Implement as a readonly struct.</summary>
internal interface IWsApiResultParser<TValue>
{
    /// <summary>Parses the result. Throws JsonException when it has an unexpected shape.</summary>
    static abstract TValue Parse(ReadOnlySpan<byte> result);
}
