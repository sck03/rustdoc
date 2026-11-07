//! Observable migration work; cancellation never removes the durable recovery journal.
use std::sync::{
    Mutex,
    atomic::{AtomicBool, Ordering},
};

#[derive(Clone, Default, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Snapshot {
    pub(crate) stage: &'static str,
    pub(crate) completed_bytes: u64,
    pub(crate) total_bytes: Option<u64>,
    pub(crate) error: Option<String>,
}

pub(crate) struct MigrationProgress {
    snapshot: Mutex<Snapshot>,
    cancelled: AtomicBool,
    started: std::time::Instant,
}

impl Default for MigrationProgress {
    fn default() -> Self {
        Self {
            snapshot: Mutex::default(),
            cancelled: AtomicBool::new(false),
            started: std::time::Instant::now(),
        }
    }
}

impl MigrationProgress {
    pub(crate) fn snapshot(&self) -> Snapshot {
        self.snapshot
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone()
    }

    pub(crate) fn stage(&self, stage: &'static str, total_bytes: Option<u64>) {
        *self
            .snapshot
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = Snapshot {
            stage,
            total_bytes,
            ..Snapshot::default()
        };
    }

    pub(crate) fn advance(&self, bytes: u64) -> Result<(), Box<dyn std::error::Error>> {
        self.check()?;
        let mut snapshot = self
            .snapshot
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        snapshot.completed_bytes = snapshot.completed_bytes.saturating_add(bytes);
        Ok(())
    }

    pub(crate) fn check(&self) -> Result<(), Box<dyn std::error::Error>> {
        if self.cancelled.load(Ordering::Acquire) {
            return Err("迁移已暂停，原数据和恢复标记已保留。再次启动程序可继续迁移。".into());
        }
        if self.started.elapsed() >= std::time::Duration::from_secs(3600) {
            return Err(
                "本次迁移已达到一小时执行上限，恢复标记已保留。再次启动程序可继续。".into(),
            );
        }
        Ok(())
    }

    pub(crate) fn cancel(&self) {
        self.cancelled.store(true, Ordering::Release);
    }

    pub(crate) fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::Acquire)
    }

    pub(crate) fn fail(&self, error: String) {
        self.snapshot
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .error = Some(error);
    }
}
