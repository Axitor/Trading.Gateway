using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Time;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Connection;

/// <summary>Outgoing message limit of one connection over a sliding one-second window. Not thread-safe: used under the send lock.</summary>
internal sealed class WsMessageBudget
{
    // Completion times of the last sends, the oldest at _next.
    private readonly long[] _sentTicks;
    private readonly long _windowTicks;
    private int _next;

    /// <summary>
    /// Creates a budget from the exchange limit per second. Zero or less means no limit.
    /// Frames the runtime sends on its own (keep-alive pings, pongs to server pings) are reserved out of the limit.
    /// </summary>
    public WsMessageBudget(int limitPerSecond, TimeSpan keepAliveInterval)
    {
        _windowTicks = SysTime.TicksFromMillis(SysTime.MillisPerSecond());

        if (limitPerSecond <= 0)
        {
            _sentTicks = [];
            return;
        }

        int capacity = Math.Max(1, limitPerSecond - RuntimeFramesPerSecond(keepAliveInterval));
        _sentTicks = new long[capacity];

        // Start with an empty window: every entry is already a full window old.
        Array.Fill(_sentTicks, SysTime.GetTimestamp() - _windowTicks);
    }

    /// <summary>Whether one more message fits now; otherwise how long to wait.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanSend(long nowTicks, out long waitMillis)
    {
        if (_sentTicks.Length == 0)
        {
            waitMillis = 0;
            return true;
        }

        long freeAtTicks = _sentTicks[_next] + _windowTicks;

        if (freeAtTicks <= nowTicks)
        {
            waitMillis = 0;
            return true;
        }

        waitMillis = SysTime.MillisFromTicks(freeAtTicks - nowTicks) + 1;
        return false;
    }

    /// <summary>Records a message once its send has completed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long nowTicks)
    {
        if (_sentTicks.Length == 0) return;

        _sentTicks[_next] = nowTicks;

        if (++_next == _sentTicks.Length) _next = 0;
    }

    /// <summary>Most frames the runtime can send by itself within one window: keep-alive pings plus a pong to a server ping.</summary>
    private static int RuntimeFramesPerSecond(TimeSpan keepAliveInterval)
    {
        long intervalMillis = (long)keepAliveInterval.TotalMilliseconds;

        // Pongs to server pings go out even with keep-alive off.
        if (intervalMillis <= 0) return 1;

        return (int)((SysTime.MillisPerSecond() + intervalMillis - 1) / intervalMillis) + 1;
    }
}
