using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;

/// <summary>Unrecoverable stream failure. The connector must stop; user intervention is required.</summary>
public sealed class WsStreamFatalException : Exception
{
    /// <summary>Why the stream connection could not be kept. A handler exception is in InnerException.</summary>
    public WsDisconnectInfo Disconnect { get; }

    /// <summary>Exchange error code of a refused control request, or zero.</summary>
    public int ExchangeCode { get; }

    /// <inheritdoc />
    public WsStreamFatalException(in WsDisconnectInfo disconnect, int exchangeCode = 0, string? message = null)
        : base(message ?? "Fatal exchange or infrastructure error on a stream. The connector must stop.", disconnect.Fault)
    {
        Disconnect = disconnect;
        ExchangeCode = exchangeCode;
    }
}
