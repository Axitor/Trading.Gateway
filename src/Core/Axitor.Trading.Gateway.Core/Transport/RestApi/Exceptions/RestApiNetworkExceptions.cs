using System.Net.Sockets;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;

/// <summary>Digs a socket-level error out of an HttpRequestException.</summary>
internal static class RestApiNetworkExceptions
{
    public static bool TryGetSocketError(HttpRequestException exception, out SocketError socketError)
    {
        if (exception.InnerException is SocketException direct)
        {
            socketError = direct.SocketErrorCode;
            return true;
        }

        if (exception.InnerException is IOException io && io.InnerException is SocketException nested)
        {
            socketError = nested.SocketErrorCode;
            return true;
        }

        socketError = default;
        return false;
    }
}
