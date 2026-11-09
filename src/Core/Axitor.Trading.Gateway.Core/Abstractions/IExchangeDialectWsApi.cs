using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Everything about the WebSocket API protocol that differs between exchanges.</summary>
internal interface IExchangeDialectWsApi : IExchangeDialectWsHandshake
{
    /// <summary>Tells a response from a pushed event and reads the response id, zero when absent. Runs on the receive loop.</summary>
    static abstract WsInboundKind ReadInbound(ReadOnlySpan<byte> message, out long requestId);

    /// <summary>Writes the request envelope up to where its params begin.</summary>
    static abstract void WriteRequestStart(ref PayloadBuilder builder, long requestId, ReadOnlySpan<byte> method);

    /// <summary>Closes the params and the envelope.</summary>
    static abstract void WriteRequestEnd(ref PayloadBuilder builder);

    /// <summary>Reads the response envelope on the caller and hands its rate limits to the limiter. Market is opaque to the core.</summary>
    static abstract void ReadResponse<TLimiter>(TLimiter limiter, ReadOnlySpan<byte> message, int market, bool captureMessage, bool captureData, out WsApiResponseEnvelope envelope) where TLimiter : IRateLimiter;

    /// <summary>Classifies an exchange error code to resiliency action. Market is opaque to the core.</summary>
    static abstract RequestErrorAction ClassifyExchangeCodeAction(int exchangeCode, int market, bool isRetryable);
}
