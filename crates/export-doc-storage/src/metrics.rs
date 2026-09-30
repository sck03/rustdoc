//! Fixed-size, value-free latency counters; no SQL, credentials or record data.
use serde_json::{Value, json};
use std::{
    sync::atomic::{AtomicU64, Ordering::Relaxed},
    time::Instant,
};

const BOUNDS_US: [u64; 12] = [
    100,
    500,
    1_000,
    5_000,
    10_000,
    50_000,
    100_000,
    500_000,
    1_000_000,
    5_000_000,
    25_000_000,
    u64::MAX,
];
#[derive(Default)]
pub struct Metrics {
    count: AtomicU64,
    failures: AtomicU64,
    active: AtomicU64,
    total_us: AtomicU64,
    max_us: AtomicU64,
    buckets: [AtomicU64; 12],
}
pub struct Observation<'a> {
    metrics: &'a Metrics,
    started: Instant,
    failed: bool,
}
impl Metrics {
    pub fn start(&self) -> Observation<'_> {
        self.active.fetch_add(1, Relaxed);
        Observation {
            metrics: self,
            started: Instant::now(),
            failed: true,
        }
    }
    pub fn snapshot(&self) -> Value {
        let counts: Vec<_> = self.buckets.iter().map(|v| v.load(Relaxed)).collect();
        let count = counts.iter().sum::<u64>();
        let quantile = |percent: u64| {
            let target = count.saturating_mul(percent).div_ceil(100);
            let mut seen = 0;
            if count == 0 {
                return 0;
            }
            for (bound, n) in BOUNDS_US.iter().zip(&counts) {
                seen += n;
                if seen >= target {
                    return (*bound).min(self.max_us.load(Relaxed));
                }
            }
            self.max_us.load(Relaxed)
        };
        json!({"completed":self.count.load(Relaxed),"failures":self.failures.load(Relaxed),"active":self.active.load(Relaxed),
            "totalMicroseconds":self.total_us.load(Relaxed),"maxMicroseconds":self.max_us.load(Relaxed),
            "p95UpperBoundMicroseconds":quantile(95),"p99UpperBoundMicroseconds":quantile(99),
            "buckets":BOUNDS_US.iter().zip(counts).map(|(upper,count)|json!({"upperBoundMicroseconds":(*upper!=u64::MAX).then_some(*upper),"count":count})).collect::<Vec<_>>()})
    }
}
impl Observation<'_> {
    pub fn finish(mut self, success: bool) {
        self.failed = !success;
    }
}
impl Drop for Observation<'_> {
    fn drop(&mut self) {
        let micros = self.started.elapsed().as_micros().min(u128::from(u64::MAX)) as u64;
        self.metrics.total_us.fetch_add(micros, Relaxed);
        self.metrics.max_us.fetch_max(micros, Relaxed);
        self.metrics.buckets[BOUNDS_US.iter().position(|bound| micros <= *bound).unwrap()]
            .fetch_add(1, Relaxed);
        if self.failed {
            self.metrics.failures.fetch_add(1, Relaxed);
        }
        self.metrics.count.fetch_add(1, Relaxed);
        self.metrics.active.fetch_sub(1, Relaxed);
    }
}
