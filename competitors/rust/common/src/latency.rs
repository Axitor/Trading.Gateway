//! Message counter and latency samples: now minus the event time, on the stub's clock in microseconds.

use std::sync::atomic::{AtomicBool, AtomicI64, AtomicUsize, Ordering};

use crate::qpc;

/// Counts messages and keeps the latency of each one. Shared by the client's callbacks and the harness.
pub struct LatencyCollector {
    samples: Box<[AtomicI64]>,
    received: AtomicI64,
    first_ticks: AtomicI64,
    last_ticks: AtomicI64,
    gaps: AtomicI64,
    resyncs: AtomicI64,
    sample_count: AtomicUsize,
    is_sampling: AtomicBool,
}

impl LatencyCollector {
    /// Writing every slot commits the memory before the run.
    pub fn new(capacity: usize) -> Self {
        Self {
            samples: (0..capacity).map(|_| AtomicI64::new(0)).collect(),
            received: AtomicI64::new(0),
            first_ticks: AtomicI64::new(0),
            last_ticks: AtomicI64::new(0),
            gaps: AtomicI64::new(0),
            resyncs: AtomicI64::new(0),
            sample_count: AtomicUsize::new(0),
            is_sampling: AtomicBool::new(false),
        }
    }

    /// One message with its event time in stub microseconds. Zero or less counts it without a sample.
    #[inline]
    pub fn record(&self, event_micros: i64) {
        let ticks = qpc::timestamp();
        self.mark(ticks);

        if event_micros <= 0 || !self.is_sampling.load(Ordering::Relaxed) {
            return;
        }

        let index = self.sample_count.fetch_add(1, Ordering::Relaxed);

        if let Some(slot) = self.samples.get(index) {
            slot.store(qpc::to_micros(ticks) - event_micros, Ordering::Relaxed);
        }
    }

    /// One message that carries no event time.
    #[inline]
    pub fn count(&self) {
        self.mark(qpc::timestamp());
    }

    pub fn add_gap(&self) {
        self.gaps.fetch_add(1, Ordering::Relaxed);
    }

    pub fn add_resync(&self) {
        self.resyncs.fetch_add(1, Ordering::Relaxed);
    }

    /// Starts keeping samples; messages before it are the warmup.
    pub fn start_sampling(&self) {
        self.is_sampling.store(true, Ordering::Release);
    }

    pub fn received(&self) -> i64 {
        self.received.load(Ordering::Acquire)
    }

    /// Timestamp of the first message, or zero before it.
    pub fn first_ticks(&self) -> i64 {
        self.first_ticks.load(Ordering::Acquire)
    }

    /// Timestamp of the latest message.
    pub fn last_ticks(&self) -> i64 {
        self.last_ticks.load(Ordering::Acquire)
    }

    /// Connection breaks seen by the client.
    pub fn gaps(&self) -> i64 {
        self.gaps.load(Ordering::Acquire)
    }

    /// Times a synced local order book lost its sync.
    pub fn resyncs(&self) -> i64 {
        self.resyncs.load(Ordering::Acquire)
    }

    /// Whether more messages were sampled than the capacity holds.
    pub fn is_truncated(&self) -> bool {
        self.samples.len() < self.sample_count.load(Ordering::Acquire)
    }

    /// Kept samples, sorted. Call once the run is over.
    pub fn sorted_samples(&self) -> Vec<i64> {
        let count = self.sample_count.load(Ordering::Acquire).min(self.samples.len());
        let mut sorted: Vec<i64> = self.samples[..count].iter().map(|slot| slot.load(Ordering::Relaxed)).collect();
        sorted.sort_unstable();
        sorted
    }

    #[inline]
    fn mark(&self, ticks: i64) {
        if self.first_ticks.load(Ordering::Relaxed) == 0 {
            let _ = self.first_ticks.compare_exchange(0, ticks, Ordering::AcqRel, Ordering::Relaxed);
        }

        self.last_ticks.store(ticks, Ordering::Release);
        self.received.fetch_add(1, Ordering::AcqRel);
    }
}

/// Nearest-rank percentile of sorted samples, e.g. 0.99.
pub fn percentile(sorted: &[i64], fraction: f64) -> i64 {
    let rank = (fraction * sorted.len() as f64).ceil() as i64;
    let index = (rank - 1).clamp(0, sorted.len() as i64 - 1);
    sorted[index as usize]
}