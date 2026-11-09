using System.Net;
using System.Net.WebSockets;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

/// <summary>Opens WebSocket connections over one shared HTTP invoker.</summary>
internal sealed class WsConnectionFactory : IDisposable
{
    private readonly HttpMessageInvoker _invoker;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _keepAliveInterval;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly int _sendBufferBytes;
    private readonly int _receiveBufferBytes;
    private readonly int _maxMessageBytes;

    // Sent with every handshake when set, e.g. an API key.
    private readonly string? _requestHeaderName;
    private readonly string? _requestHeaderValue;

    /// <summary>Creates a factory with the transport's connection settings. A request header, when given, goes with every handshake.</summary>
    public WsConnectionFactory(
        TimeSpan connectTimeout,
        TimeSpan keepAliveInterval,
        TimeSpan keepAliveTimeout,
        int sendBufferBytes,
        int receiveBufferBytes,
        int maxMessageBytes,
        string? requestHeaderName = null,
        string? requestHeaderValue = null)
    {
        _connectTimeout = connectTimeout;
        _keepAliveInterval = keepAliveInterval;
        _keepAliveTimeout = keepAliveTimeout;
        _sendBufferBytes = sendBufferBytes;
        _receiveBufferBytes = receiveBufferBytes;
        _maxMessageBytes = maxMessageBytes;
        _requestHeaderName = requestHeaderName;
        _requestHeaderValue = requestHeaderValue;

        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
    }

    /// <summary>Cold path. Opens a connection; on failure the result carries the reason and handshake details.</summary>
    public async ValueTask<WsConnectResult> ConnectAsync<TDialect>(Uri uri, int epoch, int messagesPerSecond, CancellationToken cancellationToken)
        where TDialect : struct, IExchangeDialectWsHandshake
    {
        ClientWebSocket socket = new();
        socket.Options.KeepAliveInterval = _keepAliveInterval;
        socket.Options.KeepAliveTimeout = _keepAliveTimeout;
        socket.Options.CollectHttpResponseDetails = true;

        if (_requestHeaderName is not null)
        {
            socket.Options.SetRequestHeader(_requestHeaderName, _requestHeaderValue);
        }

        using CancellationTokenSource connectSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectSource.CancelAfter(_connectTimeout);

        try
        {
            await socket.ConnectAsync(uri, _invoker, connectSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(socket, WsDisconnectReason.AppShutdown);
        }
        catch (OperationCanceledException)
        {
            return Failure(socket, WsDisconnectReason.ConnectTimeout);
        }
        catch (WebSocketException exception)
        {
            return HandshakeFailure<TDialect>(socket, exception);
        }

        // Handshake details are kept only for failures.
        socket.HttpResponseHeaders = null;

        return new WsConnectResult()
        {
            Connection = new WsConnection(socket, epoch, _sendBufferBytes, _receiveBufferBytes, _maxMessageBytes, messagesPerSecond, _keepAliveInterval),
        };
    }

    /// <summary>A response status goes to the dialect; no response means the network failed before the upgrade.</summary>
    private static WsConnectResult HandshakeFailure<TDialect>(ClientWebSocket socket, WebSocketException exception)
        where TDialect : struct, IExchangeDialectWsHandshake
    {
        WsConnectResult result = new()
        {
            StatusCode = (int)socket.HttpStatusCode,
        };

        if (result.StatusCode == 0)
        {
            result.Reason = WsNetworkExceptions.Classify(exception, out result.HttpError, out result.WebSocketError, out result.SocketError);
        }
        else
        {
            TDialect.ReadHandshakeFailure(result.StatusCode, socket.HttpResponseHeaders, out result.Reason, out result.RetryAfterSeconds);
        }

        socket.Dispose();
        return result;
    }

    private static WsConnectResult Failure(ClientWebSocket socket, WsDisconnectReason reason)
    {
        socket.Dispose();
        return new WsConnectResult() { Reason = reason };
    }

    /// <summary>Dispose IDisposable resources.</summary>
    public void Dispose() => _invoker.Dispose();
}
