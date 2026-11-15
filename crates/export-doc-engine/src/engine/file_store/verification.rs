//! Bounded verification metadata for immutable local objects. Every request opens
//! its own cursor and checks the handle's identity and mutation stamp.
use super::*;
#[cfg(test)]
#[path = "tests/verification.rs"]
mod tests;
use crate::paths::file_stamp;
use std::{
    collections::BTreeMap,
    sync::{
        Mutex,
        atomic::{AtomicU64, Ordering::Relaxed},
    },
    time::Duration,
};

#[derive(Default)]
pub(super) struct Verified {
    entries: Mutex<BTreeMap<String, (file_stamp::Stamp, u64)>>,
    flights: super::flights::Flights,
    used: AtomicU64,
}
impl Verified {
    pub(super) fn open(&self, path: &Path, object: &FileObject) -> Result<fs::File> {
        let flight = self.flights.get(&object.digest)?;
        let _checking = flight.write(Duration::from_secs(
            super::super::backup_io::TIMEOUT_SECONDS,
        ))?;
        let mut file =
            fs::File::open(path).map_err(|_| unavailable("已登记文件对象缺失或不可读。"))?;
        let stamp = file_stamp::read(&file)?;
        if stamp.size != object.size {
            return Err(unavailable("文件对象完整性校验失败。"));
        }
        {
            let mut entries = self
                .entries
                .lock()
                .map_err(|_| unavailable("文件校验缓存异常。"))?;
            let next = self.used.fetch_add(1, Relaxed);
            if let Some((previous, used)) = entries.get_mut(&object.digest) {
                if *previous == stamp {
                    *used = next;
                    return Ok(file);
                }
            }
            entries.remove(&object.digest);
        }
        verify(&mut file, object)?;
        if file_stamp::read(&file)? != stamp {
            return Err(unavailable("文件在完整性校验期间发生变化。"));
        }
        file.seek(SeekFrom::Start(0))?;
        let mut entries = self
            .entries
            .lock()
            .map_err(|_| unavailable("文件校验缓存异常。"))?;
        if entries.len() >= 1024 {
            let oldest = entries
                .iter()
                .min_by_key(|(_, (_, used))| *used)
                .map(|(digest, _)| digest.clone())
                .unwrap();
            entries.remove(&oldest);
        }
        let next = self.used.fetch_add(1, Relaxed);
        entries.insert(object.digest.clone(), (stamp, next));
        Ok(file)
    }
}
