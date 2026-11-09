//! Result tables: streams in the .NET harness format, requests with the BenchmarkDotNet columns.

use crate::bench::Measurement;
use crate::latency::percentile;

/// Request table header.
pub const REQUEST_HEADER: &str = "| Client | Benchmark | Mean µs | Error µs | StdDev µs | Allocated B/op | Allocs/op |";

pub const REQUEST_SEPARATOR: &str = "|---|---|---:|---:|---:|---:|---:|";

/// One request table row.
pub fn request_row(client: &str, benchmark: &str, measurement: &Measurement) -> String {
    format!(
        "| {client} | {benchmark} | {:.2} | {:.2} | {:.2} | {:.0} | {:.1} |",
        measurement.mean_micros,
        measurement.error_micros,
        measurement.std_dev_micros,
        measurement.bytes_per_operation,
        measurement.allocations_per_operation
    )
}

/// Table header, the same columns as the .NET harness.
pub const STREAM_HEADER: &str =
    "| Client | Stream | Msgs | Msg/s | p50 µs | p90 µs | p99 µs | p99.9 µs | Max µs | B/msg | Gen0 | Gen1 | Gen2 | GC pause ms |";

pub const STREAM_SEPARATOR: &str = "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|";

/// One table row. Rust has no GC, so its columns hold a dash.
pub fn stream_row(client: &str, stream: &str, messages: i64, messages_per_second: f64, sorted: &[i64], bytes_per_message: f64) -> String {
    format!(
        "| {client} | {stream} | {messages} | {messages_per_second:.0} | {} | {bytes_per_message:.0} | - | - | - | - |",
        latency_cells(sorted)
    )
}

/// p50, p90, p99, p99.9 and max as table cells.
pub fn latency_cells(sorted: &[i64]) -> String {
    if sorted.is_empty() {
        return "n/a | n/a | n/a | n/a | n/a".to_string();
    }

    format!(
        "{} | {} | {} | {} | {}",
        percentile(sorted, 0.50),
        percentile(sorted, 0.90),
        percentile(sorted, 0.99),
        percentile(sorted, 0.999),
        sorted[sorted.len() - 1]
    )
}