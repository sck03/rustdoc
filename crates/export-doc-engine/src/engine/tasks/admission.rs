//! Reserve bounded retry preparation before loading persisted workbook bytes.
use super::{Jobs, Runtime};
use crate::engine::error::{Result, error, unavailable};
use std::sync::{Arc, Mutex};

pub(super) struct Preparation(Arc<Mutex<Runtime>>);

impl Jobs {
    pub(super) fn prepare_retry(&self) -> Result<Preparation> {
        acquire(&self.runtime)
    }
}

fn acquire(runtime: &Arc<Mutex<Runtime>>) -> Result<Preparation> {
    crate::operation::check()?;
    let mut state = runtime
        .lock()
        .map_err(|_| unavailable("文件任务状态异常。"))?;
    if let Some(failure) = &state.failure {
        return Err(unavailable(failure.clone()));
    }
    if state.stopping {
        return Err(unavailable("程序正在关闭，不能准备重试任务。"));
    }
    if state.active.len() >= 2 || state.preparing_retries >= 2 {
        return Err(error(429, "文件任务正在准备或执行，请稍后重试。"));
    }
    state.preparing_retries += 1;
    Ok(Preparation(Arc::clone(runtime)))
}

impl Drop for Preparation {
    fn drop(&mut self) {
        if let Ok(mut state) = self.0.lock() {
            state.preparing_retries -= 1;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::operation::OperationScope;
    use std::{sync::mpsc, time::Duration};

    #[test]
    fn concurrent_preparations_are_bounded_and_drop_restores_capacity() {
        let runtime = Arc::new(Mutex::new(Runtime::default()));
        let first = acquire(&runtime).unwrap();
        let (entered, ready) = mpsc::channel();
        let (release, released) = mpsc::channel();
        let concurrent = runtime.clone();
        let worker = std::thread::spawn(move || {
            let _second = acquire(&concurrent).unwrap();
            entered.send(()).unwrap();
            released.recv_timeout(Duration::from_secs(5)).unwrap();
        });
        ready.recv_timeout(Duration::from_secs(5)).unwrap();
        assert_eq!(runtime.lock().unwrap().preparing_retries, 2);
        assert_eq!(
            acquire(&runtime)
                .err()
                .expect("third preparation must be rejected")
                .status,
            Some(429)
        );
        assert_eq!(runtime.lock().unwrap().preparing_retries, 2);
        drop(first);
        let replacement = acquire(&runtime).unwrap();
        assert_eq!(runtime.lock().unwrap().preparing_retries, 2);
        drop(replacement);
        release.send(()).unwrap();
        worker.join().unwrap();
        assert_eq!(runtime.lock().unwrap().preparing_retries, 0);
    }

    #[test]
    fn errors_cancellation_and_shutdown_never_keep_preparation_slots() {
        let runtime = Arc::new(Mutex::new(Runtime::default()));
        let denied: Result<()> = (|| {
            let _preparation = acquire(&runtime)?;
            Err(error(403, "模拟授权拒绝。"))
        })();
        assert_eq!(denied.unwrap_err().status, Some(403));
        assert_eq!(runtime.lock().unwrap().preparing_retries, 0);
        let scope = OperationScope::new(Duration::from_secs(5));
        let canceled = scope.run(|| {
            let _preparation = acquire(&runtime)?;
            scope.cancel();
            crate::operation::check()
        });
        assert_eq!(canceled.unwrap_err().status, Some(504));
        assert_eq!(runtime.lock().unwrap().preparing_retries, 0);
        let held = acquire(&runtime).unwrap();
        runtime.lock().unwrap().stopping = true;
        assert_eq!(
            acquire(&runtime)
                .err()
                .expect("shutdown must reject retries")
                .status,
            Some(503)
        );
        drop(held);
        assert_eq!(runtime.lock().unwrap().preparing_retries, 0);
    }

    #[test]
    fn full_worker_capacity_rejects_preparation_before_loading_input() {
        let mut state = Runtime::default();
        for id in ["first", "second"] {
            state
                .active
                .insert(id.into(), OperationScope::new(Duration::from_secs(5)));
        }
        let runtime = Arc::new(Mutex::new(state));
        assert_eq!(
            acquire(&runtime)
                .err()
                .expect("full workers must reject retries")
                .status,
            Some(429)
        );
        assert_eq!(runtime.lock().unwrap().preparing_retries, 0);
    }
}
