//! The stub's clock: machine-wide monotonic ticks and microseconds, rounded as Stopwatch is rounded in the .NET harness.

use std::sync::OnceLock;

static FREQUENCY: OnceLock<i64> = OnceLock::new();
static MICROS_PER_TICK: OnceLock<f64> = OnceLock::new();

/// Raw ticks of the monotonic clock.
#[inline]
pub fn timestamp() -> i64 {
    sys::counter()
}

/// Ticks per second.
#[inline]
pub fn frequency() -> i64 {
    *FREQUENCY.get_or_init(sys::frequency)
}

/// Now in microseconds on the stub's clock.
#[inline]
pub fn now_micros() -> i64 {
    to_micros(timestamp())
}

/// Ticks to microseconds with the stub's formula, so both sides round alike.
#[inline]
pub fn to_micros(ticks: i64) -> i64 {
    (ticks as f64 * micros_per_tick()) as i64
}

/// Seconds from one timestamp to another.
#[inline]
pub fn seconds_between(start_ticks: i64, end_ticks: i64) -> f64 {
    (end_ticks - start_ticks) as f64 / frequency() as f64
}

/// Seconds since a timestamp.
#[inline]
pub fn elapsed_seconds(start_ticks: i64) -> f64 {
    seconds_between(start_ticks, timestamp())
}

#[inline]
fn micros_per_tick() -> f64 {
    *MICROS_PER_TICK.get_or_init(|| 1_000_000.0 / frequency() as f64)
}

#[cfg(windows)]
mod sys {
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn QueryPerformanceCounter(count: *mut i64) -> i32;
        fn QueryPerformanceFrequency(frequency: *mut i64) -> i32;
    }

    #[inline]
    pub fn counter() -> i64 {
        let mut count = 0_i64;

        // Never fails on Windows XP and later.
        unsafe { QueryPerformanceCounter(&mut count) };
        count
    }

    pub fn frequency() -> i64 {
        let mut frequency = 0_i64;
        unsafe { QueryPerformanceFrequency(&mut frequency) };
        frequency
    }
}

// CLOCK_MONOTONIC in nanoseconds, the clock Stopwatch reads on Linux; only for building and trying the harness off Windows.
#[cfg(not(windows))]
mod sys {
    use std::ffi::c_long;

    const CLOCK_MONOTONIC: i32 = 1;

    #[repr(C)]
    struct Timespec {
        seconds: c_long,
        nanoseconds: c_long,
    }

    unsafe extern "C" {
        fn clock_gettime(clock: i32, time: *mut Timespec) -> i32;
    }

    #[inline]
    pub fn counter() -> i64 {
        let mut time = Timespec { seconds: 0, nanoseconds: 0 };
        unsafe { clock_gettime(CLOCK_MONOTONIC, &mut time) };
        time.seconds as i64 * 1_000_000_000 + time.nanoseconds as i64
    }

    pub fn frequency() -> i64 {
        1_000_000_000
    }
}