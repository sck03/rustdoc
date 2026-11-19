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
    available: Option<Arc<dyn Fn() + Send + Sync>>,
}
pub struct Reservation {
    budget: Arc<Budget>,
    bytes: usize,
}
impl Budget {
    pub fn with_availability_callback(callback: impl Fn() + Send + Sync + 'static) -> Self {
        Self {
            available: Some(Arc::new(callback)),
            ..Default::default()
        }
    }
    pub fn reserve(self: &Arc<Self>, bytes: usize) -> Result<Reservation> {
        if bytes == 0 || bytes > 64 * 1024 * 1024 {
            return Err(error(503, "任务结果大小无效。"));
        }
        self.reserve_working(bytes)
    }
    pub fn reserve_working(self: &Arc<Self>, bytes: usize) -> Result<Reservation> {
        self.try_reserve_working(bytes)?
            .ok_or_else(|| self.exhausted())
    }
    pub fn try_reserve_working(self: &Arc<Self>, bytes: usize) -> Result<Option<Reservation>> {
        if bytes == 0 || bytes > CAPACITY {
            return Err(error(503, "文件处理大小超出可用容量。"));
        }
        if self
            .used
            .try_update(Relaxed, Relaxed, |used| {
                used.checked_add(bytes).filter(|total| *total <= CAPACITY)
            })
            .is_err()
        {
            return Ok(None);
        }
        Ok(Some(Reservation {
            budget: self.clone(),
            bytes,
        }))
    }
    pub fn exhausted(&self) -> crate::api::ApiError {
        self.rejected.fetch_add(1, Relaxed);
        error(429, "响应缓冲正在使用，请稍后重试。")
    }
    fn release(&self, bytes: usize) {
        if bytes == 0 {
            return;
        }
        self.used.fetch_sub(bytes, Relaxed);
        if let Some(available) = &self.available {
            available();
        }
    }
    pub fn snapshot(&self) -> serde_json::Value {
        serde_json::json!({"capacityBytes":CAPACITY,"reservedBytes":self.used.load(Relaxed),"rejections":self.rejected.load(Relaxed)})
    }
}
impl Drop for Reservation {
    fn drop(&mut self) {
        self.budget.release(self.bytes);
    }
}
impl Reservation {
    pub fn grow_to(&mut self, bytes: usize) -> Result<()> {
        if bytes > self.bytes {
            let mut additional = self.budget.reserve_working(bytes - self.bytes)?;
            self.bytes += additional.bytes;
            additional.bytes = 0;
        }
        Ok(())
    }
    pub fn shrink_to(&mut self, bytes: usize) {
        if bytes < self.bytes {
            self.budget.release(self.bytes - bytes);
            self.bytes = bytes;
        }
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
