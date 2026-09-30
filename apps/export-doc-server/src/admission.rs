//! Fair, bounded request admission. Reservations follow blocking work so a
//! disconnected caller cannot create unbounded background execution.
use crate::telemetry::Telemetry;
use export_doc_engine::api::ApiError;
use std::{sync::Arc, time::Duration};
use tokio::sync::{OwnedSemaphorePermit, Semaphore};
#[cfg(test)]
mod tests;

pub(crate) struct Admission {
    active: Arc<Semaphore>,
    reservations: Arc<Semaphore>,
    limit: usize,
    queued_limit: usize,
    timeout: Duration,
}
pub(crate) struct Permit {
    _active: OwnedSemaphorePermit,
    _reservation: OwnedSemaphorePermit,
}
impl Admission {
    pub fn new(active: usize, queued: usize, timeout: Duration) -> Self {
        Self {
            active: Arc::new(Semaphore::new(active)),
            reservations: Arc::new(Semaphore::new(active + queued)),
            limit: active,
            queued_limit: queued,
            timeout,
        }
    }
    pub async fn acquire(&self, telemetry: &Telemetry) -> Result<Permit, ApiError> {
        let wait = telemetry.admission_wait();
        let busy = || {
            telemetry.reject(false);
            ApiError {
                status: Some(429),
                message: "请求队列繁忙，请稍后重试。".into(),
            }
        };
        let reservation = self
            .reservations
            .clone()
            .try_acquire_owned()
            .map_err(|_| busy())?;
        let active = tokio::time::timeout(self.timeout, self.active.clone().acquire_owned())
            .await
            .map_err(|_| busy())?
            .map_err(|_| busy())?;
        wait.finish(true);
        Ok(Permit {
            _active: active,
            _reservation: reservation,
        })
    }
    pub fn available(&self) -> usize {
        self.active.available_permits()
    }
    pub fn queued(&self) -> usize {
        (self.limit + self.queued_limit - self.reservations.available_permits())
            .saturating_sub(self.limit - self.active.available_permits())
            .min(self.queued_limit)
    }
}
