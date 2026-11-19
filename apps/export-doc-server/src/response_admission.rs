//! Fair, cancel-safe admission before commands mutate data. The execution queue bounds waiters.
use export_doc_engine::{
    api::ApiError,
    engine::backup_io::{Budget, Reservation},
};
use std::{sync::Arc, time::Duration};
use tokio::sync::{Notify, Semaphore};

// Upfront maxima must leave room for concurrent reads to encode their actual bytes.
const READ_HEADROOM: usize = 8 * 1024 * 1024;

pub(crate) struct Admission {
    pub budget: Arc<Budget>,
    available: Arc<Notify>,
    turn: Semaphore,
}
impl Admission {
    pub fn new() -> Self {
        let available = Arc::new(Notify::new());
        let signal = available.clone();
        Self {
            budget: Arc::new(Budget::with_availability_callback(move || {
                signal.notify_waiters()
            })),
            available,
            turn: Semaphore::new(1),
        }
    }
    pub async fn reserve(&self, bytes: usize) -> Result<Reservation, ApiError> {
        self.wait(bytes, Duration::from_secs(5)).await
    }
    async fn wait(&self, bytes: usize, timeout: Duration) -> Result<Reservation, ApiError> {
        tokio::time::timeout(timeout, async {
            let _turn = self
                .turn
                .acquire()
                .await
                .map_err(|_| self.budget.exhausted())?;
            loop {
                let ready = self.available.notified();
                tokio::pin!(ready);
                ready.as_mut().enable();
                if let Some(reservation) = self.budget.try_reserve_working(bytes, READ_HEADROOM)? {
                    return Ok(reservation);
                }
                ready.await;
            }
        })
        .await
        .map_err(|_| self.budget.exhausted())?
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    async fn overlap_waits_for_release_and_cancellation_leaves_no_reservation() {
        let admission = Arc::new(Admission::new());
        let first = admission.budget.reserve_working(64 * 1024 * 1024).unwrap();
        let mut second = admission.budget.reserve_working(64 * 1024 * 1024).unwrap();
        let pending = tokio::spawn({
            let admission = admission.clone();
            async move { admission.reserve(64 * 1024 * 1024).await }
        });
        tokio::task::yield_now().await;
        assert!(!pending.is_finished());
        assert_eq!(admission.budget.snapshot()["rejections"], 0);
        second.shrink_to(1024);
        drop(first);
        let third = pending.await.unwrap().unwrap();
        let cancelled = tokio::spawn({
            let admission = admission.clone();
            async move { admission.reserve(64 * 1024 * 1024).await }
        });
        tokio::task::yield_now().await;
        cancelled.abort();
        assert!(matches!(cancelled.await, Err(error) if error.is_cancelled()));
        drop((second, third));
        assert_eq!(admission.budget.snapshot()["reservedBytes"], 0);
        drop(admission.reserve(64 * 1024 * 1024).await.unwrap());
    }
    #[tokio::test]
    async fn command_maxima_leave_space_for_reads_while_another_command_waits() {
        let admission = Arc::new(Admission::new());
        let first = admission.reserve(64 * 1024 * 1024).await.unwrap();
        let waiting = tokio::spawn({
            let admission = admission.clone();
            async move { admission.reserve(64 * 1024 * 1024).await }
        });
        tokio::task::yield_now().await;
        assert!(!waiting.is_finished());
        let reads: Vec<_> = (0..32)
            .map(|_| admission.budget.reserve_working(64 * 1024).unwrap())
            .collect();
        assert_eq!(admission.budget.snapshot()["rejections"], 0);
        drop(first);
        let second = waiting.await.unwrap().unwrap();
        drop((second, reads));
        assert_eq!(admission.budget.snapshot()["reservedBytes"], 0);
    }
    #[tokio::test]
    async fn exhausted_wait_has_a_deadline_and_counts_one_rejection() {
        let admission = Admission::new();
        let held = admission.budget.reserve_working(128 * 1024 * 1024).unwrap();
        let error = admission
            .wait(1, Duration::from_millis(20))
            .await
            .err()
            .unwrap();
        assert_eq!(error.status, Some(429));
        assert_eq!(admission.budget.snapshot()["rejections"], 1);
        drop(held);
    }
}
