using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Time;

namespace Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;

/// <summary>Holds the ban deadline set by an exchange rate-limiter or firewall response.</summary>
internal sealed class BanGate
{
    private long _banEndTicks;

    /// <summary>Whether sending is currently forbidden, and for how much longer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBanned(out long waitBanMillis)
    {
        long end = Volatile.Read(ref _banEndTicks);
        long now = SysTime.GetTimestamp();

        if (now < end)
        {
            waitBanMillis = SysTime.MillisFromTicks(end - now) + 1;
            return true;
        }

        waitBanMillis = 0;
        return false;
    }

    /// <summary>
    /// Records a ban lasting at least delayMillis.
    /// A shorter ban than the one already in force is ignored rather than shortening it.
    /// </summary>
    public void SetBanMillis(long delayMillis)
    {
        if (delayMillis <= 0)
        {
            return;
        }

        long candidate = SysTime.GetTimestamp() + SysTime.TicksFromMillis(delayMillis);
        long current = Volatile.Read(ref _banEndTicks);

        while (candidate > current)
        {
            long actual = Interlocked.CompareExchange(ref _banEndTicks, candidate, current);

            if (actual == current)
            {
                return;
            }

            current = actual;
        }
    }
}
