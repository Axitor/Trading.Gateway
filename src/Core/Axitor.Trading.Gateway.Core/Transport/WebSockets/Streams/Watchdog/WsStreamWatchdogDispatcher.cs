using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Watchdog;

/// <summary>Stamps the time of every watchdog message. Zero means not armed.</summary>
internal readonly struct WsStreamWatchdogDispatcher : IWsStreamDispatcher
{
    private readonly StrongBox<long> _lastTicks;

    public WsStreamWatchdogDispatcher(StrongBox<long> lastTicks) => _lastTicks = lastTicks;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data)
    {
        Volatile.Write(ref _lastTicks.Value, SysTime.GetTimestamp());
        return WsDispatchResult.Handled;
    }

    /// <inheritdoc />
    public WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message) => WsInboundSignal.None;

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => Volatile.Write(ref _lastTicks.Value, 0);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap) { }

    /// <inheritdoc />
    public void OnSubscribing(int subscription) { }

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) { }
}
