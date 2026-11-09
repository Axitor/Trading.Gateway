using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

/// <summary>One subscribe or unsubscribe request for a list of stream names.</summary>
internal readonly struct WsStreamControlFrame<TDialect> : IPayloadWriter where TDialect : struct, IExchangeDialectWsStream
{
    private readonly byte[][] _names;
    private readonly int _count;
    private readonly long _requestId;
    private readonly bool _subscribe;

    public WsStreamControlFrame(byte[][] names, int count, long requestId, bool subscribe)
    {
        _names = names;
        _count = count;
        _requestId = requestId;
        _subscribe = subscribe;
    }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        TDialect.WriteControlStart(ref builder, _requestId, _subscribe);

        for (int i = 0; i < _count; i++)
        {
            TDialect.WriteControlName(ref builder, _names[i], i == 0);
        }

        TDialect.WriteControlEnd(ref builder);
    }
}
