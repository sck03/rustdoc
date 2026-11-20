//! FIFO within capacity classes, with independent admission for small commands.
//! Reservations still precede mutation; the execution queue bounds all waiters.
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
    turns: [Semaphore; 4],
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
            turns: std::array::from_fn(|_| Semaphore::new(1)),
        }
    }
    pub async fn reserve(&self, bytes: usize) -> Result<Reservation, ApiError> {
        self.wait(bytes, Duration::from_secs(5)).await
    }
    async fn wait(&self, bytes: usize, timeout: Duration) -> Result<Reservation, ApiError> {
        tokio::time::timeout(timeout, async {
            let class = match bytes {
                0..=65_536 => 0,
                65_537..=1_048_576 => 1,
                1_048_577..=8_388_608 => 2,
                _ => 3,
            };
            let _turn = self.turns[class]
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
    async fn waiting_large_command_does_not_block_small_commands() {
        let admission = Arc::new(Admission::new());
        let first = admission.reserve(64 * 1024 * 1024).await.unwrap();
        let queued = admission.clone();
        let waiting = tokio::spawn(async move { queued.reserve(64 * 1024 * 1024).await });
        tokio::task::yield_now().await;
        assert!(!waiting.is_finished());
        let login = admission
            .wait(8 * 1024 * 1024, Duration::from_millis(100))
            .await
            .unwrap();
        let save = admission
            .wait(64 * 1024, Duration::from_millis(100))
            .await
            .unwrap();
        assert!(!waiting.is_finished());
        drop((first, login, save));
        drop(waiting.await.unwrap().unwrap());
        assert_eq!(admission.budget.snapshot()["reservedBytes"], 0);
    }
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
