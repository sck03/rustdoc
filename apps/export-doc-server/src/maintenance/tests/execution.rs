use super::*;
#[tokio::test(flavor = "current_thread")]
async fn timeout_keeps_blocking_work_bounded_without_stalling_runtime() {
    let executor = Executor(Arc::new(Semaphore::new(1)));
    let (release, wait) = std::sync::mpsc::channel();
    let result = executor
        .run(Duration::from_millis(50), move || {
            let _ = wait.recv_timeout(Duration::from_secs(2));
            export_doc_engine::operation::check()
        })
        .await;
    assert_eq!(result.unwrap_err().status, Some(504));
    assert_eq!(
        executor
            .run(Duration::from_secs(1), || Ok(()))
            .await
            .unwrap_err()
            .status,
        Some(429)
    );
    release.send(()).unwrap();
    tokio::time::timeout(Duration::from_secs(2), async {
        while executor.0.available_permits() == 0 {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    executor
        .run(Duration::from_secs(1), || Ok(()))
        .await
        .unwrap();
}
