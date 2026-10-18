use super::*;
use crate::paths::RuntimePaths;

#[test]
fn settings_wake_one_worker_and_disabling_or_closing_joins_it() {
    let root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime/exchange-tests")
        .join(crate::paths::nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let service = NativeService::open(RuntimePaths {
        app_root: root.clone(),
        data_root: root.clone(),
        cache_root: root.join("Cache"),
        log_root: root.join("Logs"),
        font_path: root.join("font.otf"),
    })
    .unwrap();
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
    std::fs::remove_dir_all(root).unwrap();
}
