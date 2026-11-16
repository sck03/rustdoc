//! Sensitive replay fields are process-local, bounded, and never serialized to the database.
use super::*;
use crate::engine::error::conflict;
use std::collections::VecDeque;
#[cfg(test)]
#[path = "tests/sensitive.rs"]
mod tests;

#[derive(Default)]
pub(super) struct Inputs(VecDeque<(String, Value)>);
impl Inputs {
    pub(super) fn insert(&mut self, key: String, fields: Value) {
        // Each entry is limited to 64 KiB by Replay; 128 entries cap retained input at 8 MiB.
        self.remove(&key);
        while self.0.len() >= 128 {
            self.0.pop_front();
        }
        self.0.push_back((key, fields));
    }
    pub(super) fn get(&self, key: &str) -> Result<Value> {
        self.0
            .iter()
            .find(|(id, _)| id == key)
            .map(|(_, fields)| fields.clone())
            .ok_or_else(|| conflict("此任务的敏感输入已释放，请返回原页面重新填写并提交。"))
    }
    pub(super) fn remove(&mut self, key: &str) {
        self.0.retain(|(id, _)| id != key);
    }
}
