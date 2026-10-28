use super::*;

#[test]
fn terminal_reads_wait_for_cleanup_and_cleanup_failure_stays_visible() {
    for fail in [false, true] {
        let fixture = Fixture::new();
        let (store, jobs) = fixture.open();
        let job = persistence::create(&store, &fixture.actor, "Export", "发布边界").unwrap();
        let id = job["jobId"].as_str().unwrap();
        let (ready, committed) = mpsc::channel();
        let (release, cleanup) = mpsc::channel();
        let (result, observed) = mpsc::channel();
        std::thread::scope(|scope| {
            let jobs = &jobs;
            let store = &store;
            let fixture = &fixture;
            let writer = scope.spawn(move || {
                jobs.completion.finish(|| {
                    persistence::finish(
                        &store,
                        &fixture.actor,
                        id,
                        Ok(TaskOutput::file(
                            "output.pdf".into(),
                            "application/pdf",
                            b"%PDF-output".to_vec(),
                        )),
                    )?;
                    ready.send(()).unwrap();
                    cleanup.recv_timeout(Duration::from_secs(5)).unwrap();
                    if fail {
                        Err(unavailable("模拟输出清理失败"))
                    } else {
                        Ok(())
                    }
                })
            });
            committed.recv_timeout(Duration::from_secs(5)).unwrap();
            assert_eq!(
                persistence::get(&store, &fixture.actor, id).unwrap()["status"],
                "Succeeded"
            );
            scope.spawn(|| {
                result.send(jobs.get(&fixture.actor, id)).unwrap();
            });
            assert!(observed.recv_timeout(Duration::from_millis(100)).is_err());
            let canceled = OperationScope::new(Duration::from_secs(5));
            canceled.cancel();
            assert_eq!(
                canceled
                    .run_result(|| jobs.get(&fixture.actor, id))
                    .unwrap_err()
                    .status,
                Some(504)
            );
            release.send(()).unwrap();
            assert_eq!(writer.join().unwrap().is_err(), fail);
            let snapshot = observed.recv_timeout(Duration::from_secs(5)).unwrap();
            if fail {
                assert_eq!(snapshot.unwrap_err().status, Some(503));
                assert_eq!(
                    jobs.list(&fixture.actor, &[]).unwrap_err().status,
                    Some(503)
                );
                assert_eq!(
                    jobs.download(&fixture.actor, id).err().unwrap().status,
                    Some(503)
                );
                assert_eq!(
                    jobs.clear_finished(&fixture.actor).unwrap_err().status,
                    Some(503)
                );
            } else {
                assert_eq!(snapshot.unwrap()["status"], "Succeeded");
            }
        });
    }
}
