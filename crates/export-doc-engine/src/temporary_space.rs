//! Account for concurrent, unwritten upload bytes on the injected cache volume.
use crate::api::{ApiError, ErrorCode};
use crate::engine::error::unavailable;
use std::{
    path::PathBuf,
    sync::{Arc, Mutex},
};

const HEADROOM: u64 = 256 * 1024 * 1024;
const WRITE_RESERVATION: u64 = 1024 * 1024;

#[derive(Debug)]
pub struct Budget {
    root: PathBuf,
    pending: Arc<Mutex<u64>>,
    #[cfg(test)]
    available: Option<u64>,
}

impl Budget {
    pub fn new(root: PathBuf) -> Self {
        Self {
            root,
            pending: Arc::new(Mutex::new(0)),
            #[cfg(test)]
            available: None,
        }
    }
    /// All destinations of this runtime share pending reservations. Counting
    /// separate volumes conservatively cannot spend the database's headroom.
    pub fn at(&self, root: PathBuf) -> Arc<Self> {
        Arc::new(Self {
            root,
            pending: self.pending.clone(),
            #[cfg(test)]
            available: self.available,
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
    #[cfg(test)]
    pub(crate) fn with_available(root: PathBuf, available: u64) -> Self {
        Self {
            available: Some(available),
            ..Self::new(root)
        }
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
        if bytes.len() as u64 > self.reservation.remaining {
            self.reservation
                .ensure((bytes.len() as u64).max(WRITE_RESERVATION))
                .map_err(std::io::Error::other)?;
        }
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
        if bytes > 0 && bytes <= self.remaining {
            return Ok(());
        }
        let budget = self.budget.clone();
        let mut pending = budget
            .pending
            .lock()
            .map_err(|_| unavailable("上传空间预算不可用，请联系管理员检查运行状态。"))?;
        #[cfg(test)]
        if let Some(available) = budget.available {
            return self.ensure_available(&mut pending, bytes, available);
        }
        let available = fs2::available_space(&budget.root).map_err(|_| {
            unavailable("无法检查上传暂存空间，请核对运行数据目录的挂载与访问权限。")
        })?;
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
            return Err(unavailable(
                "上传暂存空间不足，需为运行数据保留 256 MiB；请释放空间后重试。",
            )
            .coded(ErrorCode::FileSpace)
            .at(
                "storage",
                "扩容运行数据所在磁盘，或清理已确认无用的临时文件后重新执行。",
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
    fn writer_reserves_in_blocks_and_releases_unwritten_capacity() {
        use std::io::Write;
        let budget = Arc::new(Budget::with_available(
            PathBuf::new(),
            HEADROOM + WRITE_RESERVATION,
        ));
        let mut writer = budget.writer(std::io::sink()).unwrap();
        writer.write_all(&[1; 32]).unwrap();
        assert_eq!(*budget.pending.lock().unwrap(), WRITE_RESERVATION - 32);
        writer.write_all(&[2; 32]).unwrap();
        assert_eq!(*budget.pending.lock().unwrap(), WRITE_RESERVATION - 64);
        drop(writer);
        assert_eq!(*budget.pending.lock().unwrap(), 0);
        let budget = Arc::new(Budget::with_available(PathBuf::new(), HEADROOM));
        assert!(
            budget
                .writer(std::io::sink())
                .unwrap()
                .write_all(&[1])
                .is_err()
        );
        assert_eq!(*budget.pending.lock().unwrap(), 0);
    }

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
            let error = upload
                .ensure_available(&mut budget.pending.lock().unwrap(), bytes, available)
                .unwrap_err();
            let public = error.public_body("disk-budget-test");
            assert_eq!(public["code"], "FILE_SPACE_EXHAUSTED");
            assert!(public["message"].as_str().unwrap().contains("256 MiB"));
            assert_eq!(public["issues"][0]["field"], "storage");
            assert_eq!(public["outcome"], "NotCommitted");
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
