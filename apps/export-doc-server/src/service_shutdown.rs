//! Bounds the host's final wait, including blocking storage cleanup.
use export_doc_engine::engine::NativeService;
use std::{
    sync::{Arc, mpsc},
    thread,
    time::Duration,
};

pub fn close_service(service: Arc<NativeService>) -> Result<(), String> {
    bounded(Duration::from_secs(10), move || {
        service.close().map_err(|error| error.to_string())
    })
}

fn bounded(
    timeout: Duration,
    close: impl FnOnce() -> Result<(), String> + Send + 'static,
) -> Result<(), String> {
    let (sender, receiver) = mpsc::sync_channel(1);
    thread::Builder::new()
        .name("service-cleanup".into())
        .spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(close))
                .unwrap_or_else(|_| Err("服务清理线程异常退出。".into()));
            let _ = sender.send(result);
        })
        .map_err(|error| error.to_string())?;
    receiver
        .recv_timeout(timeout)
        .map_err(|_| "服务清理超过等待时限，不能确认清理完成。".to_owned())?
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn blocked_cleanup_does_not_block_the_host_and_failures_are_preserved() {
        let (release, blocked) = mpsc::channel();
        let result = bounded(Duration::from_millis(20), move || {
            blocked.recv().unwrap();
            Ok(())
        });
        assert!(result.unwrap_err().contains("不能确认"));
        release.send(()).unwrap();
        assert_eq!(
            bounded(Duration::from_secs(1), || Err("storage failure".into())),
            Err("storage failure".into())
        );
        assert!(bounded(Duration::from_secs(1), || panic!("worker failure")).is_err());
    }
}
