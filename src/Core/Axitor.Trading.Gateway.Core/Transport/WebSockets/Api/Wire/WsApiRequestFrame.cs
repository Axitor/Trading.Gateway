using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

/// <summary>One WebSocket API request: the dialect's envelope around the endpoint params and auth.</summary>
internal readonly struct WsApiRequestFrame<TDialect, TRequest, TAuth> : IWsApiFrameWriter
    where TDialect : struct, IExchangeDialectWsApi
    where TRequest : struct, IWsApiRequestWriter
    where TAuth : struct, IWsApiAuthenticator
{
    private const int QueryBufferBytes = 1024;
    private const int AuthBufferBytes = 256;

    private readonly TRequest _request;
    private readonly TAuth _auth;

    public WsApiRequestFrame(TRequest request, TAuth auth)
    {
        _request = request;
        _auth = auth;
    }

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder, long requestId)
    {
        TDialect.WriteRequestStart(ref builder, requestId, TRequest.Method);

        Span<byte> query = TAuth.Signs ? stackalloc byte[QueryBufferBytes] : default;
        Span<byte> pending = stackalloc byte[AuthBufferBytes];

        WsApiRequestBuilder request = new(builder.FreeSpan(), query, pending);
        _auth.AddParams(ref request);
        _request.Write(ref request);
        request.Finish();
        _auth.AppendSignature(ref request);

        builder.Advance(request.Length);
        TDialect.WriteRequestEnd(ref builder);
    }
}
