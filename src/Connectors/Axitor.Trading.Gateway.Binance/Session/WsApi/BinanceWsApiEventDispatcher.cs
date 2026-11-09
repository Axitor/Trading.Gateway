using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.WsApi;

/// <summary>Events of the shared WebSocket API connections. Only serverShutdown is known; anything else is reported as unknown.</summary>
/// <remarks>
/// DESIGN NOTE: Spot announces a restart with {"event":{"e":"serverShutdown","E":...}} before it drops the connection, with no fixed lead time.
/// The lane then replaces the connection make-before-break. Futures push no events. The event is rare, so it is read with a JSON reader.
/// </remarks>
internal readonly struct BinanceWsApiEventDispatcher : IWsApiEventDispatcher
{
    /// <inheritdoc />
    public WsDispatchResult Dispatch(ReadOnlySpan<byte> message, int connectionEpoch, out WsInboundSignal signal)
    {
        if (IsServerShutdown(message))
        {
            signal = WsInboundSignal.ServerShutdown;
            return WsDispatchResult.Handled;
        }

        signal = WsInboundSignal.None;
        return WsDispatchResult.Unreadable;
    }

    /// <inheritdoc />
    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) { }

    /// <inheritdoc />
    public void OnReconnected(in WsStreamGap gap) { }

    /// <summary>Cold path. Whether the event object carries e = serverShutdown.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsServerShutdown(ReadOnlySpan<byte> message)
    {
        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("event"u8))
                {
                    reader.Read();
                    return reader.TokenType == JsonTokenType.StartObject && IsShutdownEvent(ref reader);
                }

                reader.Read();
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // Malformed: reported as unknown.
        }

        return false;
    }

    /// <summary>Reads the event object the reader is on until its type.</summary>
    private static bool IsShutdownEvent(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("e"u8))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("serverShutdown"u8);
            }

            reader.Read();
            reader.Skip();
        }

        return false;
    }
}
