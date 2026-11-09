using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Binance.RateLimiter;

/// <summary>A rate limit window: a cap, a length, and the weight spent inside the current period.</summary>
/// <remarks>
/// DESIGN NOTE: Period and weight are packed into one long, so the lock-free pre-check before a raise never sees a torn value.
/// Every change runs under the limiter's lock (ADR-0007).
/// </remarks>
internal sealed class BinanceRateLimitWindow
{
    private long _packedState;
    private readonly long _windowPeriodLengthMillis;
    private readonly int _windowMaxWeightLimit;

    /// <summary>Creates a window with max weight limit and length of a period in milliseconds.</summary>
    public BinanceRateLimitWindow(int maxWeightLimit, long periodMillis)
    {
        _windowMaxWeightLimit = maxWeightLimit;
        _windowPeriodLengthMillis = periodMillis;
    }

    /// <summary>The period index and spent weight are bit-packed into a single 64-bit integer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(int period, int weight) => ((long)period << 32) | (uint)weight;

    /// <summary>Performs lock-free reconciliation of the window weight against exchange rate limit headers.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool NeedsRaise(long nowMillis, int weight)
    {
        if (weight <= 0) return false;

        int period = (int)(nowMillis / _windowPeriodLengthMillis);
        long state = Volatile.Read(ref _packedState);

        int currentPeriod = (int)(state >> 32);
        int currentWeight = (int)state;

        return currentPeriod != period || currentWeight < weight;
    }

    /// <summary>Raises the count to what the exchange reported, never lowers it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Raise(long nowMillis, int weight)
    {
        int period = (int)(nowMillis / _windowPeriodLengthMillis);
        int currentPeriod = (int)(_packedState >> 32);
        int currentWeight = (int)_packedState;

        if (currentPeriod != period)
        {
            _packedState = Pack(period, weight);
            return;
        }

        if (currentWeight < weight)
        {
            _packedState = Pack(period, weight);
        }
    }

    /// <summary>Reports whether the weight would fit, without charging it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanCharge(long nowMillis, int weight, out long waitMillis)
    {
        int period = (int)(nowMillis / _windowPeriodLengthMillis);
        int currentPeriod = (int)(_packedState >> 32);
        int currentWeight = (int)_packedState;

        int actualUsed = (currentPeriod == period) ? currentWeight : 0;

        if (actualUsed + weight <= _windowMaxWeightLimit)
        {
            waitMillis = 0;
            return true;
        }

        long periodStart = (long)period * _windowPeriodLengthMillis;
        waitMillis = periodStart + _windowPeriodLengthMillis - nowMillis;
        return false;
    }

    /// <summary>Charges weight against the period.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Charge(long nowMillis, int weight)
    {
        int period = (int)(nowMillis / _windowPeriodLengthMillis);
        int currentPeriod = (int)(_packedState >> 32);
        int currentWeight = (int)_packedState;

        if (currentPeriod != period)
        {
            _packedState = Pack(period, weight);
        }
        else
        {
            _packedState = Pack(period, currentWeight + weight);
        }
    }

}
