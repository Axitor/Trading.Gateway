//! Stream harness: one client on one stream type over the stub's replay, the same rules and output as the .NET harness.
//!
//! Start the stub with pace 1 for latency or max for throughput. The stub replays once per connection.
//! With a warmup pass the client takes a whole pass first and closes; a new client in the same process then
//! measures a fresh pass from its first message. Without one, the warmup is the start of the measured pass.
//!
//! The harness runs on the main thread and only polls the collector; the client's own threads do the work.

use std::error::Error;
use std::sync::Arc;
use std::thread;
use std::time::Duration;

use crate::latency::LatencyCollector;
use crate::{alloc, qpc, report};

/// Error of a client call.
pub type BoxError = Box<dyn Error + Send + Sync>;

/// Symbols the recorder took the streams of.
pub const STREAM_SYMBOLS: [&str; 20] = [
    "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "BNBUSDT", "ADAUSDT", "LINKUSDT", "AVAXUSDT", "SUIUSDT",
    "LTCUSDT", "DOTUSDT", "BCHUSDT", "TRXUSDT", "NEARUSDT", "APTUSDT", "ARBUSDT", "OPUSDT", "FILUSDT", "ETCUSDT",
];

const WARMUP_PASS_OPTION: &str = "--warmup-pass";
const WARMUP_MESSAGES_OPTION: &str = "--warmup-msgs";
const WINDOW_OPTION: &str = "--window";
const DEFAULT_WARMUP_MESSAGES: i64 = 50_000;
const SAMPLE_CAPACITY: usize = 4 * 1024 * 1024;

// Samples of the warmup pass are dropped, so one slot is enough.
const WARMUP_SAMPLE_CAPACITY: usize = 1;

const WARMUP_SECONDS: f64 = 3.0;
const END_SILENCE_SECONDS: f64 = 3.0;
const FIRST_MESSAGE_TIMEOUT_SECONDS: f64 = 120.0;
const POLL_INTERVAL: Duration = Duration::from_millis(50);

/// Stream type of a harness run.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum StreamKind {
    BookTicker,
    AggTrade,
    /// Diff depth at 100 ms.
    Depth,
    /// Top 20 levels at 100 ms.
    Depth20,
    /// One-minute klines.
    Kline,
    /// Local order books built from diff depth at 100 ms.
    Book,
}

impl StreamKind {
    pub fn parse(value: &str) -> Option<Self> {
        match value.to_ascii_lowercase().as_str() {
            "bookticker" => Some(Self::BookTicker),
            "aggtrade" => Some(Self::AggTrade),
            "depth" => Some(Self::Depth),
            "depth20" => Some(Self::Depth20),
            "kline" => Some(Self::Kline),
            "book" => Some(Self::Book),
            _ => None,
        }
    }

    /// Name in the result table.
    pub fn name(self) -> &'static str {
        match self {
            Self::BookTicker => "bookTicker",
            Self::AggTrade => "aggTrade",
            Self::Depth => "depth@100ms",
            Self::Depth20 => "depth20@100ms",
            Self::Kline => "kline_1m",
            Self::Book => "book (depth@100ms)",
        }
    }
}

/// A client under test. The harness calls it from the main thread.
pub trait StreamClient {
    /// A running subscription; stop ends it.
    type Run;

    fn supports(&self, kind: StreamKind) -> bool;

    /// Whether the client reports connection breaks to the collector.
    fn tracks_gaps(&self) -> bool;

    /// Connects, subscribes every stream symbol and feeds each message to the collector.
    fn start(&mut self, kind: StreamKind, collector: Arc<LatencyCollector>) -> Result<Self::Run, BoxError>;

    fn stop(&mut self, run: Self::Run);
}

/// Host and port of the exchange stub. AXITOR_STUB_HOST overrides it.
pub fn stub_host() -> String {
    std::env::var("AXITOR_STUB_HOST").unwrap_or_else(|_| "127.0.0.1:5100".to_string())
}

/// Arguments after "streams": client, stream, then --warmup-pass, --warmup-msgs N, --window seconds (0 to the end of the pass). Returns the exit code.
pub fn run_streams<C: StreamClient>(client_name: &str, client: &mut C, args: &[String]) -> i32 {
    let Some(arguments) = Arguments::parse(client_name, args) else {
        eprintln!(
            "Usage: streams {client_name} <bookticker|aggtrade|depth|depth20|kline|book> [--warmup-pass] [--warmup-msgs N] [--window seconds]"
        );
        return 1;
    };

    if !client.supports(arguments.kind) {
        eprintln!("{client_name} has no {} run.", arguments.kind.name());
        return 1;
    }

    if arguments.warmup_pass {
        if let Err(error) = run_warmup_pass(client, arguments.kind) {
            eprintln!("Warmup pass failed: {error}");
            return 1;
        }
    }

    let collector = Arc::new(LatencyCollector::new(SAMPLE_CAPACITY));

    let run = match client.start(arguments.kind, Arc::clone(&collector)) {
        Ok(run) => run,
        Err(error) => {
            eprintln!("Start failed: {error}");
            return 1;
        }
    };

    println!(
        "{client_name}, {}: {} symbols subscribed, waiting for the stub's pass.",
        arguments.kind.name(),
        STREAM_SYMBOLS.len()
    );

    let measured = measure(&collector, arguments.warmup_messages, arguments.window_seconds, arguments.warmup_pass);
    client.stop(run);

    let window = match measured {
        Ok(Some(window)) if window.end.received != window.start.received => window,
        Ok(_) => {
            println!(
                "Nothing measured: the pass ended inside the warmup or never synced. Received {}, gaps {}, resyncs {}.",
                collector.received(),
                collector.gaps(),
                collector.resyncs()
            );
            return 1;
        }
        Err(error) => {
            eprintln!("{error}");
            return 1;
        }
    };

    print_report(client_name, &arguments, client.tracks_gaps(), &collector, &window);
    0
}

/// One whole pass with the same code as the measured one.
fn run_warmup_pass<C: StreamClient>(client: &mut C, kind: StreamKind) -> Result<(), BoxError> {
    let collector = Arc::new(LatencyCollector::new(WARMUP_SAMPLE_CAPACITY));
    let run = client.start(kind, Arc::clone(&collector))?;
    let measured = measure(&collector, 0, 0, true);
    client.stop(run);
    measured?;

    println!("Warmup pass: {} msgs.", collector.received());
    Ok(())
}

/// Waits out the warmup, then follows the pass until it goes silent or the window is full. None when the pass ended inside the warmup.
fn measure(collector: &LatencyCollector, warmup_messages: i64, window_seconds: u32, from_first_message: bool) -> Result<Option<Window>, String> {
    let wait_start = qpc::timestamp();
    let mut start = Snapshot::default();
    let mut end = Snapshot::default();
    let mut is_measuring = false;

    if from_first_message {
        start = Snapshot::take(collector, qpc::timestamp());
        collector.start_sampling();
        end = start;
        is_measuring = true;
    }

    loop {
        thread::sleep(POLL_INTERVAL);
        let received = collector.received();

        if received == 0 {
            if FIRST_MESSAGE_TIMEOUT_SECONDS < qpc::elapsed_seconds(wait_start) {
                return Err(format!(
                    "No data in {FIRST_MESSAGE_TIMEOUT_SECONDS:.0} s. Is the stub running on {}?",
                    stub_host()
                ));
            }

            continue;
        }

        let is_silent = END_SILENCE_SECONDS <= qpc::elapsed_seconds(collector.last_ticks());

        if !is_measuring {
            if received < warmup_messages && qpc::elapsed_seconds(collector.first_ticks()) < WARMUP_SECONDS {
                if is_silent {
                    return Ok(None);
                }

                continue;
            }

            // The window opens before sampling starts, so no sampled message falls outside it.
            start = Snapshot::take(collector, qpc::timestamp());
            collector.start_sampling();
            end = start;
            is_measuring = true;
            continue;
        }

        if 0 < window_seconds && f64::from(window_seconds) <= qpc::elapsed_seconds(start.ticks) {
            return Ok(Some(Window {
                start,
                end: Snapshot::take(collector, qpc::timestamp()),
            }));
        }

        if received != end.received {
            end = Snapshot::take(collector, collector.last_ticks());
            continue;
        }

        if is_silent {
            // Before the client stops: closing it would count as gaps.
            return Ok(Some(Window {
                start,
                end: Snapshot {
                    gaps: collector.gaps(),
                    resyncs: collector.resyncs(),
                    ..end
                },
            }));
        }
    }
}

fn print_report(client_name: &str, arguments: &Arguments, tracks_gaps: bool, collector: &LatencyCollector, window: &Window) {
    let start = window.start;
    let end = window.end;

    // Measured from the first message: a window opened before the data would count the stub's settle time.
    let start_ticks = start.ticks.max(collector.first_ticks());
    let messages = end.received - start.received;
    let seconds = qpc::seconds_between(start_ticks, end.ticks);
    let bytes_per_message = (end.allocated_bytes - start.allocated_bytes) as f64 / messages as f64;
    let allocations_per_message = (end.allocations - start.allocations) as f64 / messages as f64;
    let sorted = collector.sorted_samples();

    let warmup = if arguments.warmup_pass {
        "after a warmup pass, the whole fresh pass".to_string()
    } else {
        format!(
            "warmup {} msgs (limit {} or {WARMUP_SECONDS:.0} s)",
            start.received, arguments.warmup_messages
        )
    };

    let limit = if arguments.window_seconds == 0 {
        "to the end of the pass".to_string()
    } else {
        format!("limit {} s", arguments.window_seconds)
    };

    let gaps = if tracks_gaps { end.gaps.to_string() } else { "n/a".to_string() };
    let truncated = if collector.is_truncated() { " (truncated)" } else { "" };

    println!();
    println!("{}", report::STREAM_HEADER);
    println!("{}", report::STREAM_SEPARATOR);
    println!(
        "{}",
        report::stream_row(
            client_name,
            arguments.kind.name(),
            messages,
            messages as f64 / seconds,
            &sorted,
            bytes_per_message
        )
    );
    println!(
        "Warmup: {warmup}. Window {seconds:.2} s ({limit}), latency samples {}{truncated}, gaps {gaps}, resyncs {}. Allocations {allocations_per_message:.1}/msg.",
        sorted.len(),
        end.resyncs
    );
}

struct Arguments {
    kind: StreamKind,
    warmup_pass: bool,
    warmup_messages: i64,
    window_seconds: u32,
}

impl Arguments {
    fn parse(client_name: &str, args: &[String]) -> Option<Self> {
        if args.len() < 2 || args[0] != client_name {
            return None;
        }

        let kind = StreamKind::parse(&args[1])?;
        let mut warmup_pass = false;
        let mut warmup_messages = DEFAULT_WARMUP_MESSAGES;
        let mut window_seconds = 0;
        let mut i = 2;

        while i < args.len() {
            if args[i] == WARMUP_PASS_OPTION {
                warmup_pass = true;
                i += 1;
                continue;
            }

            let value = args.get(i + 1)?;

            match args[i].as_str() {
                WARMUP_MESSAGES_OPTION => warmup_messages = i64::from(parse_count(value)?),
                WINDOW_OPTION => window_seconds = parse_count(value)?,
                _ => return None,
            }

            i += 2;
        }

        Some(Self {
            kind,
            warmup_pass,
            warmup_messages,
            window_seconds,
        })
    }
}

/// Digits only, as NumberStyles.None in the .NET harness.
fn parse_count(text: &str) -> Option<u32> {
    if text.is_empty() || !text.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }

    text.parse().ok()
}

struct Window {
    start: Snapshot,
    end: Snapshot,
}

#[derive(Clone, Copy, Default)]
struct Snapshot {
    ticks: i64,
    received: i64,
    allocated_bytes: u64,
    allocations: u64,
    gaps: i64,
    resyncs: i64,
}

impl Snapshot {
    fn take(collector: &LatencyCollector, ticks: i64) -> Self {
        Self {
            ticks,
            received: collector.received(),
            allocated_bytes: alloc::allocated_bytes(),
            allocations: alloc::allocation_count(),
            gaps: collector.gaps(),
            resyncs: collector.resyncs(),
        }
    }
}