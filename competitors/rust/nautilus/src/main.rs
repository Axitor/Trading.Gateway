//! NautilusTrader's Binance adapter against the exchange stub, at its venue level: the clients that return Binance structs.
//! Its WebSocket API client only places and cancels orders under a fixed 20 per second quota, so it is not benchmarked.
//! Run: nautilus-bench streams nautilus <stream> [--warmup-pass] [--warmup-msgs N] [--window seconds]
//!      nautilus-bench rest [part of a benchmark name]

mod rest;
mod streams;

use axitor_bench_common::alloc::CountingAllocator;

#[global_allocator]
static ALLOCATOR: CountingAllocator = CountingAllocator;

pub const CLIENT_NAME: &str = "nautilus";

/// Symbol every request asks for; the stub has its order book recorded.
pub const SYMBOL: &str = "BTCUSDT";

pub const CLIENT_ORDER_ID: &str = "bench-order-1";

// Signed calls need a key and a secret; the stub checks neither.
pub const API_KEY: &str = "stub-key";
pub const API_SECRET: &str = "stub-secret";

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();

    let code = match args.first().map(String::as_str) {
        Some("streams") => streams::run(&args[1..]),
        Some("rest") => rest::run(&args[1..]),
        _ => {
            eprintln!("Usage: streams {CLIENT_NAME} <stream> [options] | rest [name]");
            1
        }
    };

    std::process::exit(code);
}