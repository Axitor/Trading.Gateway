using System.Net.Sockets;
using System.Net.WebSockets;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Exceptions;

/// <summary>Digs the failure kind and diagnostics out of a WebSocket exception chain.</summary>
internal static class WsNetworkExceptions
{
    /// <summary>Cold path. A socket error wins over an HTTP error, which wins over a bare stream break.</summary>
    public static WsDisconnectReason Classify(Exception exception, out HttpRequestError httpError, out WebSocketError webSocketError, out SocketError socketError)
    {
        httpError = default;
        webSocketError = default;
        socketError = default;

        bool hasWebSocket = false;
        bool hasHttp = false;
        bool hasSocket = false;
        bool hasIO = false;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case WebSocketException webSocket when !hasWebSocket:
                    webSocketError = webSocket.WebSocketErrorCode;
                    hasWebSocket = true;
                    break;

                case HttpRequestException http:
                    httpError = http.HttpRequestError;
                    hasHttp = true;
                    break;

                case SocketException socket:
                    socketError = socket.SocketErrorCode;
                    hasSocket = true;
                    break;

                case IOException:
                    hasIO = true;
                    break;
            }
        }

        if (hasSocket) return WsDisconnectReason.ExceptionSocket;
        if (hasHttp) return WsDisconnectReason.ExceptionHttp;
        if (hasIO) return WsDisconnectReason.ExceptionIO;

        return WsDisconnectReason.ExceptionWebSocket;
    }
}
