//! Reserve the result's bytes before materializing a database blob.
use crate::engine::error::{Result, error};
use std::sync::{
    Arc,
    atomic::{AtomicU64, AtomicUsize, Ordering::Relaxed},
};

const CAPACITY: usize = 128 * 1024 * 1024;
#[derive(Default)]
pub struct Budget {
    used: AtomicUsize,
    rejected: AtomicU64,
}
pub struct Reservation {
    budget: Arc<Budget>,
    bytes: usize,
}
impl Budget {
    pub fn reserve(self: &Arc<Self>, bytes: usize) -> Result<Reservation> {
        if bytes == 0 || bytes > 64 * 1024 * 1024 {
            return Err(error(503, "任务结果大小无效。"));
        }
        self.used
            .try_update(Relaxed, Relaxed, |used| {
                used.checked_add(bytes).filter(|total| *total <= CAPACITY)
            })
            .map_err(|_| {
                self.rejected.fetch_add(1, Relaxed);
                error(429, "文件下载内存正在使用，请稍后重试。")
            })?;
        Ok(Reservation {
            budget: self.clone(),
            bytes,
        })
    }
    pub fn snapshot(&self) -> serde_json::Value {
        serde_json::json!({"capacityBytes":CAPACITY,"reservedBytes":self.used.load(Relaxed),"rejections":self.rejected.load(Relaxed)})
    }
}
impl Drop for Reservation {
    fn drop(&mut self) {
        self.budget.used.fetch_sub(self.bytes, Relaxed);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reservations_bound_parallel_results_and_release_on_drop() {
        let budget = Arc::new(Budget::default());
        let first = budget.reserve(64 * 1024 * 1024).unwrap();
        let second = budget.reserve(64 * 1024 * 1024).unwrap();
        assert_eq!(budget.reserve(1).err().unwrap().status, Some(429));
        drop(first);
        let small = budget.reserve(1).unwrap();
        assert_eq!(budget.snapshot()["reservedBytes"], 64 * 1024 * 1024 + 1);
        drop((second, small));
        assert_eq!(budget.snapshot()["reservedBytes"], 0);
    }
}
