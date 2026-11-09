//! Request benchmarks: an async call repeated in timed iterations, as BenchmarkDotNet does it, with time and allocations per operation.
//!
//! The first call must succeed, so a failure is never measured. A pilot sizes the iterations to about 250 ms,
//! five of them warm up, twenty are measured. Error is half the 99.9% confidence interval of the mean, as in BenchmarkDotNet.
//! A client with a built-in rate limiter gets a call budget instead of the pilot, so every call stays inside its burst.

use std::future::Future;

use crate::harness::BoxError;
use crate::{alloc, qpc, report};

const ITERATIONS: usize = 20;
const WARMUP_ITERATIONS: u64 = 5;
const ITERATION_SECONDS: f64 = 0.25;
const PILOT_SECONDS: f64 = 0.1;

// Student t for a two-sided 99.9% interval with ITERATIONS - 1 degrees of freedom.
const T_999: f64 = 3.883;

/// Time and allocations of one benchmark, per operation.
pub struct Measurement {
    pub mean_micros: f64,
    pub error_micros: f64,
    pub std_dev_micros: f64,
    pub bytes_per_operation: f64,
    pub allocations_per_operation: f64,
}

/// Whether a benchmark name passes the command line filter: a case-insensitive part of the name, or no filter.
pub fn matches(filter: Option<&str>, name: &str) -> bool {
    filter.is_none_or(|part| name.to_ascii_lowercase().contains(&part.to_ascii_lowercase()))
}

/// Measures the call and prints its table row, or a failed row. Returns whether it was measured.
pub async fn run<F, Fut>(client: &str, name: &str, operations_per_call: u32, call_budget: Option<u32>, call: F) -> bool
where
    F: FnMut() -> Fut,
    Fut: Future<Output = Result<(), BoxError>>,
{
    match measure(operations_per_call, call_budget, call).await {
        Ok(measurement) => {
            println!("{}", report::request_row(client, name, &measurement));
            true
        }
        Err(error) => {
            println!("| {client} | {name} | failed: {error} |");
            false
        }
    }
}

/// Runs the call through check, warmup and the measured iterations.
pub async fn measure<F, Fut>(operations_per_call: u32, call_budget: Option<u32>, mut call: F) -> Result<Measurement, BoxError>
where
    F: FnMut() -> Fut,
    Fut: Future<Output = Result<(), BoxError>>,
{
    call().await?;

    let (warmup_calls, calls_per_iteration) = match call_budget {
        Some(budget) => {
            let left = u64::from(budget.saturating_sub(1));
            let warmup = left / 10;
            (warmup, ((left - warmup) / ITERATIONS as u64).max(1))
        }
        None => {
            let calls = pilot(&mut call).await?;
            (calls * WARMUP_ITERATIONS, calls)
        }
    };

    run_calls(&mut call, warmup_calls).await?;

    let operations = calls_per_iteration * u64::from(operations_per_call);
    let mut per_operation = [0.0_f64; ITERATIONS];
    let start_bytes = alloc::allocated_bytes();
    let start_allocations = alloc::allocation_count();

    for slot in &mut per_operation {
        let start = qpc::timestamp();
        run_calls(&mut call, calls_per_iteration).await?;
        *slot = qpc::elapsed_seconds(start) * 1_000_000.0 / operations as f64;
    }

    let total_operations = (operations * ITERATIONS as u64) as f64;
    let mean = per_operation.iter().sum::<f64>() / ITERATIONS as f64;
    let variance = per_operation.iter().map(|value| (value - mean).powi(2)).sum::<f64>() / (ITERATIONS - 1) as f64;
    let std_dev = variance.sqrt();

    Ok(Measurement {
        mean_micros: mean,
        error_micros: T_999 * std_dev / (ITERATIONS as f64).sqrt(),
        std_dev_micros: std_dev,
        bytes_per_operation: (alloc::allocated_bytes() - start_bytes) as f64 / total_operations,
        allocations_per_operation: (alloc::allocation_count() - start_allocations) as f64 / total_operations,
    })
}

/// Calls per iteration, from how many calls fit in the pilot time.
async fn pilot<F, Fut>(call: &mut F) -> Result<u64, BoxError>
where
    F: FnMut() -> Fut,
    Fut: Future<Output = Result<(), BoxError>>,
{
    let start = qpc::timestamp();
    let mut calls = 0_u64;

    loop {
        call().await?;
        calls += 1;

        let elapsed = qpc::elapsed_seconds(start);

        if PILOT_SECONDS <= elapsed {
            return Ok(((calls as f64 * ITERATION_SECONDS / elapsed).ceil() as u64).max(1));
        }
    }
}

async fn run_calls<F, Fut>(call: &mut F, count: u64) -> Result<(), BoxError>
where
    F: FnMut() -> Fut,
    Fut: Future<Output = Result<(), BoxError>>,
{
    for _ in 0..count {
        call().await?;
    }

    Ok(())
}