//! Bounded operational telemetry, independent of durable business audit.
use export_doc_storage::metrics::{Metrics, Observation};
use serde_json::{Value, json};
use std::{
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering::Relaxed},
        mpsc::{self, SyncSender},
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub(crate) struct Telemetry {
    requests: Metrics,
    admission_wait: Metrics,
    rejected: AtomicU64,
    bulk_rejected: AtomicU64,
    dropped: AtomicU64,
    errors: Arc<AtomicU64>,
    sequence: AtomicU64,
    prefix: String,
    started: Instant,
    sender: Option<SyncSender<Vec<u8>>>,
    worker: Option<JoinHandle<()>>,
}
pub(crate) struct RequestSpan<'a> {
    telemetry: &'a Telemetry,
    observation: Option<Observation<'a>>,
    id: String,
    operation: &'static str,
    started: Instant,
    status: u16,
}
impl Telemetry {
    pub fn new(log_root: PathBuf) -> Self {
        let (sender, receiver) = mpsc::sync_channel::<Vec<u8>>(256);
        let errors = Arc::new(AtomicU64::new(0));
        let writer_errors = errors.clone();
        let worker = thread::Builder::new()
            .name("request-log".into())
            .spawn(move || {
                while let Ok(mut records) = receiver.recv() {
                    for _ in 0..63 {
                        match receiver.try_recv() {
                            Ok(record) => records.extend(record),
                            Err(_) => break,
                        }
                    }
                    if append(&log_root, &records).is_err() {
                        writer_errors.fetch_add(1, Relaxed);
                    }
                }
            });
        let worker = match worker {
            Ok(worker) => Some(worker),
            Err(_) => {
                errors.fetch_add(1, Relaxed);
                None
            }
        };
        Self {
            requests: Default::default(),
            admission_wait: Default::default(),
            rejected: AtomicU64::new(0),
            bulk_rejected: AtomicU64::new(0),
            dropped: AtomicU64::new(0),
            errors,
            sequence: AtomicU64::new(0),
            prefix: format!(
                "{:x}-{:x}",
                SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap_or_default()
                    .as_nanos(),
                std::process::id()
            ),
            started: Instant::now(),
            sender: Some(sender),
            worker,
        }
    }
    pub fn start(&self, operation: &'static str) -> RequestSpan<'_> {
        RequestSpan {
            telemetry: self,
            observation: Some(self.requests.start()),
            id: format!("{}-{:x}", self.prefix, self.sequence.fetch_add(1, Relaxed)),
            operation,
            started: Instant::now(),
            status: 499,
        }
    }
    pub fn reject(&self, bulk: bool) {
        if bulk {
            self.bulk_rejected.fetch_add(1, Relaxed);
        } else {
            self.rejected.fetch_add(1, Relaxed);
        }
    }
    pub fn admission_wait(&self) -> Observation<'_> {
        self.admission_wait.start()
    }
    pub fn snapshot(&self, available: usize, bulk_available: usize, queued: usize) -> Value {
        json!({"requests":self.requests.snapshot(),"admissionRejected":self.rejected.load(Relaxed),"bulkRejected":self.bulk_rejected.load(Relaxed),
            "queueWait":self.admission_wait.snapshot(),"queueCapacity":crate::MAX_QUEUED_REQUESTS,"queuedRequests":queued,
            "requestCapacity":crate::MAX_REQUESTS,"availableRequestSlots":available,"bulkCapacity":crate::MAX_BULK_UPLOADS,"availableBulkSlots":bulk_available,
            "uptimeSeconds":self.started.elapsed().as_secs(),"logsDropped":self.dropped.load(Relaxed),"logWriteErrors":self.errors.load(Relaxed)})
    }
}
impl RequestSpan<'_> {
    pub fn id(&self) -> &str {
        &self.id
    }
    pub fn finish(mut self, status: u16) {
        self.status = status;
    }
}
impl Drop for RequestSpan<'_> {
    fn drop(&mut self) {
        if let Some(observation) = self.observation.take() {
            observation.finish(self.status < 400);
        }
        let mut line=json!({"timestamp":chrono::Utc::now().to_rfc3339(),"requestId":self.id,"operation":self.operation,"status":self.status,"durationMicroseconds":self.started.elapsed().as_micros().min(u128::from(u64::MAX)) as u64}).to_string().into_bytes();
        line.push(b'\n');
        if self
            .telemetry
            .sender
            .as_ref()
            .is_none_or(|sender| sender.try_send(line).is_err())
        {
            self.telemetry.dropped.fetch_add(1, Relaxed);
        }
    }
}
impl Drop for Telemetry {
    fn drop(&mut self) {
        self.sender.take();
        if let Some(worker) = self.worker.take() {
            let deadline = Instant::now() + Duration::from_secs(1);
            while !worker.is_finished() && Instant::now() < deadline {
                thread::sleep(Duration::from_millis(10));
            }
            if worker.is_finished() {
                let _ = worker.join();
            }
        }
    }
}
fn append(root: &Path, record: &[u8]) -> std::io::Result<()> {
    let file = root.join("requests.jsonl");
    let previous = root.join("requests.previous.jsonl");
    for path in [&file, &previous] {
        export_doc_engine::paths::ensure_safe_absolute(path).map_err(std::io::Error::other)?;
    }
    if file.try_exists()? && fs::metadata(&file)?.len() >= 8 * 1024 * 1024 {
        if previous.try_exists()? {
            fs::remove_file(&previous)?;
        }
        fs::rename(&file, &previous)?;
    }
    OpenOptions::new()
        .create(true)
        .append(true)
        .open(file)?
        .write_all(record)
}
