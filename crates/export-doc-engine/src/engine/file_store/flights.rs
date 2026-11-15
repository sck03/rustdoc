//! Bounded, per-object admission. Idle keys disappear; network work never holds
//! the registry mutex. OperationLock supplies cancellation, deadlines and wakeups.
use super::*;
use crate::engine::operation_lock::OperationLock;
use std::{
    collections::BTreeMap,
    sync::{Arc, Mutex, Weak},
};

#[derive(Default)]
pub(super) struct Flights(Mutex<BTreeMap<String, Weak<OperationLock<()>>>>);
impl Flights {
    pub(super) fn get(&self, key: &str) -> Result<Arc<OperationLock<()>>> {
        let mut active = self
            .0
            .lock()
            .map_err(|_| unavailable("文件读取协调状态异常。"))?;
        active.retain(|_, value| value.strong_count() > 0);
        if let Some(lock) = active.get(key).and_then(Weak::upgrade) {
            return Ok(lock);
        }
        if active.len() >= 128 {
            return Err(super::super::error::error(
                429,
                "并发文件读取已满，请稍后重试。",
            ));
        }
        let lock = Arc::new(OperationLock::default());
        active.insert(key.to_owned(), Arc::downgrade(&lock));
        Ok(lock)
    }
}

#[cfg(test)]
#[path = "tests/flights.rs"]
mod tests;
