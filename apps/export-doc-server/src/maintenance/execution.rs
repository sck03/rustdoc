//! Short maintenance commands use bounded blocking workers. Polling and cancel
//! only read/publish memory snapshots and keep their own responsive path.
use crate::request::error;
use export_doc_engine::{api::ApiError, operation::OperationScope};
use std::{sync::Arc, time::Duration};
use tokio::sync::Semaphore;
pub(super) struct Executor(Arc<Semaphore>);
struct Cancellation(OperationScope);
impl Drop for Cancellation {
    fn drop(&mut self) {
        self.0.cancel();
    }
}
impl Executor {
    pub(super) fn new() -> Self {
        Self(Arc::new(Semaphore::new(2)))
    }
    pub(super) async fn run<T: Send + 'static>(
        &self,
        timeout: Duration,
        action: impl FnOnce() -> Result<T, ApiError> + Send + 'static,
    ) -> Result<T, ApiError> {
        let permit = self
            .0
            .clone()
            .try_acquire_owned()
            .map_err(|_| error(429, "维护请求正在处理，请稍后查询状态。"))?;
        let scope = OperationScope::new(timeout);
        let _cancel = Cancellation(scope.clone());
        let worker = tokio::task::spawn_blocking(move || {
            let _permit = permit;
            scope.run(|| {
                export_doc_engine::operation::check()?;
                action()
            })
        });
        tokio::time::timeout(timeout, worker)
            .await
            .map_err(|_| {
                let mut cause = error(504, "维护请求等待超时，请先查询维护状态确认是否已受理。");
                cause.outcome_unknown = true;
                cause
            })?
            .map_err(|_| {
                let mut cause = error(503, "维护请求异常中断，请查询维护状态后处理。");
                cause.outcome_unknown = true;
                cause
            })?
    }
}
#[cfg(test)]
#[path = "tests/execution.rs"]
mod tests;
