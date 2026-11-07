use super::*;

#[test]
fn captured_backup_releases_publication_and_verifies_the_open_file() {
    let mut fixture = Fixture::new();
    fixture.actor.admin = true;
    let actor = &fixture.actor;
    let (store, jobs) = fixture.open();
    let backup = store.data_root.join("Backups/download.dump");
    fs::create_dir_all(backup.parent().unwrap()).unwrap();
    let bytes = b"complete backup contents";
    fs::write(&backup, bytes).unwrap();
    let output = TaskOutput::managed_file(backup.clone(), String::new()).unwrap();
    let job = persistence::create(&store, actor, "Backup", "下载").unwrap();
    let id = job["jobId"].as_str().unwrap();
    persistence::finish(&store, actor, id, Ok(output)).unwrap();
    let check_status = |snapshot, status| {
        let result = jobs.authorize_download(actor, id, Some(snapshot));
        assert_eq!(result.unwrap_err().status, Some(status));
    };

    let (pending, snapshot) = jobs.capture_download(actor, id).unwrap();
    let (completed, published) = mpsc::channel();
    std::thread::scope(|scope| {
        scope.spawn(|| {
            completed.send(jobs.completion.finish(|| Ok(()))).unwrap();
        });
        // No file bytes have been verified yet; another publication must still finish.
        published
            .recv_timeout(Duration::from_secs(5))
            .unwrap()
            .unwrap();
    });
    let moved = backup.with_extension("saved");
    fs::rename(&backup, &moved).unwrap();
    fs::write(&backup, b"different replacement").unwrap();
    let download = pending.verify().unwrap();
    jobs.authorize_download(actor, id, Some(&snapshot)).unwrap();
    assert_eq!(download.into_memory().unwrap().content, bytes);
    fs::remove_file(&backup).unwrap();
    fs::rename(moved, &backup).unwrap();

    for changed in [
        b"corrupt backup contents!".as_slice(),
        b"longer changed backup contents".as_slice(),
    ] {
        let (pending, _) = jobs.capture_download(actor, id).unwrap();
        fs::write(&backup, changed).unwrap();
        assert_eq!(pending.verify().err().unwrap().status, Some(503));
        fs::write(&backup, bytes).unwrap();
    }
    let (pending, _) = jobs.capture_download(actor, id).unwrap();
    let canceled = OperationScope::new(Duration::from_secs(5));
    canceled.cancel();
    let result = canceled.run_result(|| pending.verify());
    assert_eq!(result.err().unwrap().status, Some(504));

    let (pending, snapshot) = jobs.capture_download(actor, id).unwrap();
    jobs.completion
        .finish(|| Err(unavailable("cleanup failed")))
        .unwrap_err();
    drop(pending.verify().unwrap());
    check_status(&snapshot, 503);
    jobs.completion.recover(|| Ok(())).unwrap();
    let mut stale = snapshot.clone();
    stale["_managedOutput"]["digest"] = json!("different restored output");
    check_status(&stale, 409);
    jobs.operation(DELETE_JOB, actor, id).unwrap();
    check_status(&snapshot, 404);
}
