using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Writes one WebSocket API request frame carrying the given id. Implement as a readonly struct.</summary>
internal interface IWsApiFrameWriter
{
    /// <summary>Writes the frame. Called under the send lock right before the frame goes out.</summary>
    void Write(ref PayloadBuilder builder, long requestId);
}
