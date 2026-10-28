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
    downloads: Metrics,
    download_timeouts: AtomicU64,
    download_disconnects: AtomicU64,
    download_read_errors: AtomicU64,
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
    failure: Option<&'static str>,
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
            downloads: Default::default(),
            download_timeouts: AtomicU64::new(0),
            download_disconnects: AtomicU64::new(0),
            download_read_errors: AtomicU64::new(0),
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
            failure: None,
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
    pub fn download_start(&self) -> Observation<'_> {
        self.downloads.start()
    }
    pub fn download_failure(&self, reason: &str) {
        match reason {
            "server-shutdown" => return,
            "idle-timeout" | "transfer-timeout" => &self.download_timeouts,
            "client-disconnected" => &self.download_disconnects,
            _ => &self.download_read_errors,
        }
        .fetch_add(1, Relaxed);
    }
    pub fn snapshot(&self, available: usize, bulk_available: usize, queued: usize) -> Value {
        json!({"requests":self.requests.snapshot(),"admissionRejected":self.rejected.load(Relaxed),"bulkRejected":self.bulk_rejected.load(Relaxed),
            "queueWait":self.admission_wait.snapshot(),"queueCapacity":crate::MAX_QUEUED_REQUESTS,"queuedRequests":queued,
            "requestCapacity":crate::MAX_REQUESTS,"availableRequestSlots":available,"bulkCapacity":crate::MAX_BULK_UPLOADS,"availableBulkSlots":bulk_available,
            "uptimeSeconds":self.started.elapsed().as_secs(),"logsDropped":self.dropped.load(Relaxed),"logWriteErrors":self.errors.load(Relaxed),
            "downloads":self.downloads.snapshot(),"downloadCapacity":crate::MAX_DOWNLOADS,"downloadTimeouts":self.download_timeouts.load(Relaxed),"downloadDisconnects":self.download_disconnects.load(Relaxed),"downloadReadErrors":self.download_read_errors.load(Relaxed)})
    }
    pub fn download_finished(&self, id: &str, failure: Option<&str>, elapsed: Duration) {
        self.record(json!({"timestamp":chrono::Utc::now().to_rfc3339(),"requestId":id,"stage":"download","failureCategory":failure,"durationMicroseconds":elapsed.as_micros().min(u128::from(u64::MAX)) as u64}));
    }
    fn record(&self, value: Value) {
        let mut line = value.to_string().into_bytes();
        line.push(b'\n');
        if self
            .sender
            .as_ref()
            .is_none_or(|sender| sender.try_send(line).is_err())
        {
            self.dropped.fetch_add(1, Relaxed);
        }
    }
}
impl RequestSpan<'_> {
    pub fn failure(&mut self, error: &export_doc_engine::api::ApiError) {
        self.failure = error.diagnostic;
    }
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
        self.telemetry.record(json!({"timestamp":chrono::Utc::now().to_rfc3339(),"requestId":self.id,"operation":self.operation,"status":self.status,"failureCategory":self.failure,"stage":"request","durationMicroseconds":self.started.elapsed().as_micros().min(u128::from(u64::MAX)) as u64}));
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
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn failure_logs_keep_category_and_request_id_without_the_sensitive_cause() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .join(".codex-runtime/telemetry-tests")
            .join(export_doc_engine::paths::nonce().unwrap());
        fs::create_dir_all(&root).unwrap();
        let telemetry = Telemetry::new(root.clone());
        let mut span = telemetry.start("GetDashboard");
        let id = span.id().to_owned();
        span.failure(&export_doc_engine::api::ApiError {
            status: Some(503),
            message: "password=SECRET; SELECT private-customer-data".into(),
            diagnostic: Some("storage"),
        });
        span.finish(503);
        drop(telemetry);
        let log = fs::read_to_string(root.join("requests.jsonl")).unwrap();
        let row: Value = serde_json::from_str(log.trim()).unwrap();
        assert_eq!(row["requestId"], id);
        assert_eq!(row["failureCategory"], "storage");
        assert_eq!(row["stage"], "request");
        assert!(!log.contains("SECRET") && !log.contains("SELECT") && !log.contains("customer"));
        fs::remove_dir_all(root).unwrap();
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
