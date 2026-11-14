//! Account for concurrent, unwritten upload bytes on the injected cache volume.
use crate::api::ApiError;
use crate::engine::error::error;
use std::{
    path::PathBuf,
    sync::{Arc, Mutex},
};

const HEADROOM: u64 = 256 * 1024 * 1024;

#[derive(Debug)]
pub struct Budget {
    root: PathBuf,
    pending: Arc<Mutex<u64>>,
}

impl Budget {
    pub fn new(root: PathBuf) -> Self {
        Self {
            root,
            pending: Arc::new(Mutex::new(0)),
        }
    }
    /// All destinations of this runtime share pending reservations. Counting
    /// separate volumes conservatively cannot spend the database's headroom.
    pub fn at(&self, root: PathBuf) -> Arc<Self> {
        Arc::new(Self {
            root,
            pending: self.pending.clone(),
        })
    }
    pub fn writer<W: std::io::Write>(
        self: &Arc<Self>,
        output: W,
    ) -> Result<SpaceWriter<W>, ApiError> {
        Ok(SpaceWriter {
            output,
            reservation: self.reserve(0)?,
        })
    }

    pub fn reserve(self: &Arc<Self>, bytes: u64) -> Result<Reservation, ApiError> {
        let mut reservation = Reservation {
            budget: self.clone(),
            remaining: 0,
        };
        reservation.ensure(bytes)?;
        Ok(reservation)
    }
}

pub struct Reservation {
    budget: Arc<Budget>,
    remaining: u64,
}
pub struct SpaceWriter<W> {
    output: W,
    reservation: Reservation,
}
impl<W: std::io::Write> std::io::Write for SpaceWriter<W> {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        self.reservation
            .ensure(bytes.len() as u64)
            .map_err(std::io::Error::other)?;
        let written = self.output.write(bytes)?;
        self.output.flush()?;
        self.reservation.committed(written as u64);
        Ok(written)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        self.output.flush()
    }
}
impl<W: std::io::Seek> std::io::Seek for SpaceWriter<W> {
    fn seek(&mut self, position: std::io::SeekFrom) -> std::io::Result<u64> {
        self.output.seek(position)
    }
}

impl Reservation {
    pub fn ensure(&mut self, bytes: u64) -> Result<(), ApiError> {
        let budget = self.budget.clone();
        let mut pending = budget
            .pending
            .lock()
            .map_err(|_| error(503, "上传空间预算不可用。"))?;
        let available =
            fs2::available_space(&budget.root).map_err(|_| error(503, "无法检查上传暂存空间。"))?;
        self.ensure_available(&mut pending, bytes, available)
    }

    fn ensure_available(
        &mut self,
        pending: &mut u64,
        bytes: u64,
        available: u64,
    ) -> Result<(), ApiError> {
        let extra = bytes.saturating_sub(self.remaining);
        let total = pending
            .checked_add(extra)
            .filter(|total| *total <= available.saturating_sub(HEADROOM));
        if available < HEADROOM || total.is_none() {
            return Err(error(
                503,
                "上传暂存空间不足，需为运行数据保留 256 MiB；请释放空间后重试。",
            ));
        }
        *pending = total.unwrap();
        self.remaining += extra;
        Ok(())
    }

    /// Called only after the async file has flushed, when the filesystem accounts for these bytes.
    pub fn committed(&mut self, bytes: u64) {
        let released = bytes.min(self.remaining);
        *self
            .budget
            .pending
            .lock()
            .unwrap_or_else(|e| e.into_inner()) -= released;
        self.remaining -= released;
    }
}

impl Drop for Reservation {
    fn drop(&mut self) {
        self.committed(self.remaining);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn concurrent_uploads_account_for_remaining_bytes_and_release_on_failure() {
        let budget = Arc::new(Budget::new(PathBuf::new()));
        let mut first = Reservation {
            budget: budget.clone(),
            remaining: 0,
        };
        let mut second = Reservation {
            budget: budget.clone(),
            remaining: 0,
        };
        let available = HEADROOM + 100;
        first
            .ensure_available(&mut budget.pending.lock().unwrap(), 60, available)
            .unwrap();
        assert!(
            second
                .ensure_available(&mut budget.pending.lock().unwrap(), 50, available)
                .is_err()
        );
        first.committed(20);
        // Written bytes already reduced free space; they must not be counted twice.
        second
            .ensure_available(&mut budget.pending.lock().unwrap(), 40, available - 20)
            .unwrap();
        assert!(
            first
                .ensure_available(&mut budget.pending.lock().unwrap(), 1, HEADROOM + 79)
                .is_err()
        );
        drop(second);
        assert_eq!(*budget.pending.lock().unwrap(), 40);
        drop(first);
        assert_eq!(*budget.pending.lock().unwrap(), 0);
    }

    #[test]
    fn unknown_length_growth_rejects_low_space_and_overflow() {
        let budget = Arc::new(Budget::new(PathBuf::new()));
        let mut upload = Reservation {
            budget: budget.clone(),
            remaining: 0,
        };
        for (bytes, available) in [(0, HEADROOM - 1), (u64::MAX, u64::MAX)] {
            assert!(
                upload
                    .ensure_available(&mut budget.pending.lock().unwrap(), bytes, available)
                    .is_err()
            );
        }
        upload
            .ensure_available(&mut budget.pending.lock().unwrap(), 10, HEADROOM + 10)
            .unwrap();
        upload.committed(10);
        assert!(
            upload
                .ensure_available(&mut budget.pending.lock().unwrap(), 1, HEADROOM)
                .is_err()
        );
    }
}
