using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;

/// <summary>Decides what follows a closed or failed connection. Makes no calls itself.</summary>
internal static class WsReconnectPolicy
{
    /// <summary>Fatal stops the transport, Retry reconnects, Skip means the connection ended as expected.</summary>
    public static RequestErrorAction Classify(WsDisconnectReason reason) =>
        reason switch
        {
            WsDisconnectReason.HandshakeBanLong
                or WsDisconnectReason.HandshakeRejected
                or WsDisconnectReason.DispatchFault
                or WsDisconnectReason.MessageUnreadable => RequestErrorAction.Fatal,

            WsDisconnectReason.None
                or WsDisconnectReason.Rotation
                or WsDisconnectReason.ServerShutdown
                or WsDisconnectReason.NetworkRepaired
                or WsDisconnectReason.AppShutdown => RequestErrorAction.Skip,

            _ => RequestErrorAction.Retry,
        };

    /// <summary>Whether the reason should set the ban gate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBanFault(WsDisconnectReason reason) => reason == WsDisconnectReason.HandshakeBanShort;

    /// <summary>Whether the reason should trip the network gate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNetworkFault(WsDisconnectReason reason) =>
        reason is WsDisconnectReason.ExceptionHttp
               or WsDisconnectReason.ExceptionSocket
               or WsDisconnectReason.ExceptionIO
               or WsDisconnectReason.KeepAliveTimeout
               or WsDisconnectReason.WatchdogTimeout;

    /// <summary>Ban length for the ban gate: Retry-After when given, the fallback otherwise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetBanMillis(int retryAfterSeconds, int fallbackMillis) =>
        0 < retryAfterSeconds ? retryAfterSeconds * 1000L : fallbackMillis;

    /// <summary>Exponential back-off for the given consecutive attempt, starting at one. Jitter is added separately.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetBackoffDelayMillis(int attempt, int baseMillis, int maxMillis)
    {
        int shift = Math.Clamp(attempt - 1, 0, 16);
        long delay = (long)baseMillis << shift;
        return 0 < maxMillis && maxMillis < delay ? maxMillis : delay;
    }

    /// <summary>Random delay that keeps reconnects from moving in waves.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetJitterMillis(int minMillis, int maxMillis) => Random.Shared.Next(minMillis, maxMillis);
}
