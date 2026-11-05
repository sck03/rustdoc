use super::*;

#[test]
fn settings_wake_one_worker_and_disabling_or_closing_joins_it() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = crate::engine::test_support::TestService::open(directory.paths());
    let scheduler = &service.exchange_scheduler;
    assert!(scheduler.worker.lock().unwrap().is_none());
    // Maintenance defers the worker before any network request. This exercises
    // real thread startup, wakeups and shutdown without contacting a public site.
    let maintenance = service.maintenance.write().unwrap();
    let mut config = settings::current(&service.store).unwrap();
    let save = |config: &Value| {
        service
            .store
            .connection()
            .unwrap()
            .set_settings("settings", 1, config)
            .unwrap()
    };
    config["exchangeRate"]["autoRefreshEnabled"] = json!(true);
    save(&config);
    scheduler.refresh(&service).unwrap();
    let identity = scheduler
        .worker
        .lock()
        .unwrap()
        .as_ref()
        .unwrap()
        .handle
        .thread()
        .id();
    assert_eq!(
        run(&service, &(Mutex::new(Signal::default()), Condvar::new())).unwrap(),
        Duration::from_secs(30)
    );
    config["exchangeRate"]["cacheDurationMinutes"] = json!(60);
    save(&config);
    scheduler.refresh(&service).unwrap();
    {
        let worker = scheduler.worker.lock().unwrap();
        let worker = worker.as_ref().unwrap();
        assert_eq!(worker.handle.thread().id(), identity);
        assert_eq!(worker.signal.0.lock().unwrap().generation, 1);
    }
    config["exchangeRate"]["autoRefreshEnabled"] = json!(false);
    save(&config);
    scheduler.refresh(&service).unwrap();
    assert!(scheduler.worker.lock().unwrap().is_none());
    config["exchangeRate"]["autoRefreshEnabled"] = json!(true);
    save(&config);
    scheduler.refresh(&service).unwrap();
    service.close().unwrap();
    assert!(scheduler.worker.lock().unwrap().is_none());
    service.close().unwrap();
    drop(maintenance);
    drop(service);
}
