using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Everything about the WebSocket stream protocol that differs between exchanges.</summary>
internal interface IExchangeDialectWsStream : IExchangeDialectWsHandshake
{
    /// <summary>Tells stream data, an exchange event and a control reply apart, and finds their bounds and route key. Runs on the receive loop.</summary>
    static abstract WsInboundKind ReadInbound(ReadOnlySpan<byte> message, bool isBinary, out WsStreamInbound inbound);

    /// <summary>Writes a subscribe or unsubscribe request up to its first stream name.</summary>
    static abstract void WriteControlStart(ref PayloadBuilder builder, long requestId, bool subscribe);

    /// <summary>Writes one stream name into the request.</summary>
    static abstract void WriteControlName(ref PayloadBuilder builder, ReadOnlySpan<byte> name, bool isFirst);

    /// <summary>Closes the request.</summary>
    static abstract void WriteControlEnd(ref PayloadBuilder builder);

    /// <summary>Cold path. Resiliency action for an error reply to a control request.</summary>
    static abstract RequestErrorAction ClassifyControlError(int exchangeCode);
}
