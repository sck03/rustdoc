//! The caller holds the process render permit; cache misses cannot render concurrently.
//! One lazy expiry worker releases idle PDFs and stops with this service.
use super::{Pdf, Result, unavailable};
use std::{
    collections::VecDeque,
    sync::{Arc, Condvar, Mutex},
    thread::JoinHandle,
    time::{Duration, Instant},
};

const TTL: Duration = Duration::from_secs(300);
const MAX_BYTES: usize = 64 * 1024 * 1024;
struct Entry {
    key: String,
    pdf: Arc<Pdf>,
    expires: Instant,
}
#[derive(Default)]
struct State {
    entries: VecDeque<Entry>,
    stopping: bool,
}
#[derive(Default)]
struct Shared {
    state: Mutex<State>,
    changed: Condvar,
}
#[derive(Default)]
pub(in crate::engine) struct Cache {
    shared: Arc<Shared>,
    worker: Mutex<Option<JoinHandle<()>>>,
}

impl Cache {
    pub(super) fn pdf(&self, key: &str, render: impl FnOnce() -> Result<Pdf>) -> Result<Arc<Pdf>> {
        crate::operation::check()?;
        {
            let mut state = self
                .shared
                .state
                .lock()
                .map_err(|_| unavailable("PDF 预览缓存异常。"))?;
            if state.stopping {
                return Err(unavailable("PDF 预览已停止。"));
            }
            state.entries.retain(|entry| entry.expires > Instant::now());
            if let Some(entry) = state.entries.iter().find(|entry| entry.key == key) {
                return Ok(entry.pdf.clone());
            }
        }
        let pdf = Arc::new(render()?);
        crate::operation::check()?;
        if pdf.bytes.len() > MAX_BYTES {
            return Ok(pdf);
        }
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("PDF 缓存清理状态异常。"))?;
        let mut state = self
            .shared
            .state
            .lock()
            .map_err(|_| unavailable("PDF 预览缓存异常。"))?;
        if state.stopping {
            return Err(unavailable("PDF 预览已停止。"));
        }
        if worker.is_none() {
            let shared = self.shared.clone();
            *worker = Some(
                std::thread::Builder::new()
                    .name("exportdoc-preview-expiry".into())
                    .spawn(move || expire(&shared))?,
            );
        }
        while state.entries.len() >= 4
            || state
                .entries
                .iter()
                .map(|entry| entry.pdf.bytes.len())
                .sum::<usize>()
                + pdf.bytes.len()
                > MAX_BYTES
        {
            state.entries.pop_front();
        }
        state.entries.push_back(Entry {
            key: key.into(),
            pdf: pdf.clone(),
            expires: Instant::now() + TTL,
        });
        self.shared.changed.notify_all();
        Ok(pdf)
    }

    pub(in crate::engine) fn stop(&self) -> Result<()> {
        let mut worker = self
            .worker
            .lock()
            .map_err(|_| unavailable("PDF 缓存清理状态异常。"))?;
        {
            let mut state = self
                .shared
                .state
                .lock()
                .unwrap_or_else(|cause| cause.into_inner());
            state.stopping = true;
            state.entries.clear();
            self.shared.changed.notify_all();
        }
        if let Some(worker) = worker.take() {
            worker
                .join()
                .map_err(|_| unavailable("PDF 缓存清理线程异常退出。"))?;
        }
        Ok(())
    }
}
impl Drop for Cache {
    fn drop(&mut self) {
        let _ = self.stop();
    }
}

fn expire(shared: &Shared) {
    let mut state = shared
        .state
        .lock()
        .unwrap_or_else(|cause| cause.into_inner());
    while !state.stopping {
        state.entries.retain(|entry| entry.expires > Instant::now());
        shared.changed.notify_all();
        state = if let Some(entry) = state.entries.front() {
            let delay = entry.expires.saturating_duration_since(Instant::now());
            shared
                .changed
                .wait_timeout(state, delay)
                .unwrap_or_else(|cause| cause.into_inner())
                .0
        } else {
            shared
                .changed
                .wait(state)
                .unwrap_or_else(|cause| cause.into_inner())
        };
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reuse_eviction_idle_expiry_size_and_shutdown_are_bounded() {
        let cache = Cache::default();
        let make = || {
            Ok(Pdf {
                bytes: vec![0; 1024],
                page_count: 2,
            })
        };
        let first = cache.pdf("one", make).unwrap();
        assert!(Arc::ptr_eq(
            &first,
            &cache.pdf("one", || panic!("must reuse PDF")).unwrap()
        ));
        for key in ["two", "three", "four", "five"] {
            cache.pdf(key, make).unwrap();
        }
        let mut state = cache.shared.state.lock().unwrap();
        assert_eq!(state.entries.len(), 4);
        assert!(state.entries.iter().all(|entry| entry.key != "one"));
        for entry in &mut state.entries {
            entry.expires = Instant::now();
        }
        cache.shared.changed.notify_all();
        let (state, _) = cache
            .shared
            .changed
            .wait_timeout_while(state, Duration::from_secs(2), |state| {
                !state.entries.is_empty()
            })
            .unwrap();
        assert!(
            state.entries.is_empty(),
            "idle entries expire without another preview request"
        );
        drop(state);
        cache
            .pdf("large", || {
                Ok(Pdf {
                    bytes: vec![0; MAX_BYTES + 1],
                    page_count: 1,
                })
            })
            .unwrap();
        assert!(cache.shared.state.lock().unwrap().entries.is_empty());
        cache.pdf("last", make).unwrap();
        cache.stop().unwrap();
        cache.stop().unwrap();
        assert!(cache.shared.state.lock().unwrap().entries.is_empty());
        assert!(cache.pdf("stopped", make).is_err());
    }
}
