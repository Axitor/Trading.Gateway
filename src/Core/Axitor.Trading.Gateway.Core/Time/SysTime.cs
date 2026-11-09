using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Time;

/// <summary>Time source: wall clock for signing, monotonic ticks for latency.</summary>
/// <remarks>
/// DESIGN NOTE: The OS high-resolution timer is far finer than network jitter, so no clock thread is needed.
/// The host keeps the wall clock right through NTP; the library does no time sync of its own (ADR-0008).
/// </remarks>
internal static class SysTime
{
    private static readonly double _ticksPerNano = (double)Stopwatch.Frequency / 1_000_000_000.0;
    private static readonly double _ticksPerMilli = (double)Stopwatch.Frequency / 1_000.0;
    private static readonly double _nanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
    private static readonly double _millisPerTick = 1_000.0 / Stopwatch.Frequency;
    private const long _nanosPerMilli = 1_000_000L;

    private const long _millisPerSecond = 1_000L;
    private const long _millisPerTenSeconds = 10_000L;
    private const long _millisPerMinute = 60_000L;
    private const long _millisPerFiveMinutes = 300_000L;
    private const long _millisPerDay = 86_400_000L;

    /// <summary>Milliseconds per ten seconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisPerTenSeconds() => _millisPerTenSeconds;

    /// <summary>Milliseconds per second.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisPerSecond() => _millisPerSecond;

    /// <summary>Milliseconds per minute.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisPerMinute() => _millisPerMinute;

    /// <summary>Milliseconds per five minutes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisPerFiveMinutes() => _millisPerFiveMinutes;

    /// <summary>Milliseconds per day.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisPerDay() => _millisPerDay;

    /// <summary>Wall clock in milliseconds since the Unix epoch, for the request timestamp.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UtcMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Starts a latency timer. Monotonic ticks, meaningless as a date.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>Nanoseconds elapsed since the tick returned by StartTimer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ElapsedNanos(long startTicks) => NanosFromTicks(Stopwatch.GetTimestamp() - startTicks);

    /// <summary>
    /// Converts a nanosecond interval to counter ticks. Designed strictly for relative durations.
    /// Passing absolute system uptime will cause floating-point precision loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long TicksFromNanos(long nanos) => (long)(nanos * _ticksPerNano);

    /// <summary>
    /// Converts a millisecond interval to counter ticks. Designed strictly for relative durations.
    /// Passing absolute system uptime will cause floating-point precision loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long TicksFromMillis(long millis) => (long)(millis * _ticksPerMilli);

    /// <summary>
    /// Converts a ticks interval to nanoseconds. Designed strictly for relative durations.
    /// Passing absolute system uptime will cause floating-point precision loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NanosFromTicks(long ticks) => (long)(ticks * _nanosPerTick);

    /// <summary>
    /// Converts a ticks interval to milliseconds. Designed strictly for relative durations.
    /// Passing absolute system uptime will cause floating-point precision loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisFromTicks(long ticks) => (long)(ticks * _millisPerTick);

    /// <summary>Converts milliseconds to nanoseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NanosFromMillis(long millis) => millis * _nanosPerMilli;

}
