use super::*;
use crate::paths::{RuntimePaths, nonce};
use std::{fs, path::PathBuf, sync::mpsc, time::Instant};

struct Fixture {
    root: PathBuf,
    paths: RuntimePaths,
    actor: Actor,
}
impl Fixture {
    fn new() -> Self {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime/task-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(&root).unwrap();
        let paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
        Self {
            root,
            paths,
            actor: Actor {
                id: 1,
                name: "任务用户".into(),
                company: "DEFAULT".into(),
                department: "GENERAL".into(),
                admin: false,
                grants: vec![],
            },
        }
    }
    fn open(&self) -> (Arc<Store>, Jobs) {
        let store = Arc::new(Store::open(&self.paths).unwrap());
        let jobs = Jobs::open(store.clone()).unwrap();
        (store, jobs)
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        fs::remove_dir_all(&self.root).unwrap();
    }
}
fn wait(jobs: &Jobs, actor: &Actor, id: &str) -> Value {
    let deadline = Instant::now() + Duration::from_secs(5);
    loop {
        let value = jobs.get(actor, id).unwrap();
        if ["Succeeded", "Failed", "Canceled"].contains(&value["status"].as_str().unwrap()) {
            return value;
        }
        assert!(Instant::now() < deadline, "task timed out");
        std::thread::sleep(Duration::from_millis(10));
    }
}

#[test]
fn output_and_snapshot_survive_restart_and_cleanup_is_idempotent() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let bytes = b"%PDF-1.7\nNative test output".to_vec();
    let output = bytes.clone();
    let started = jobs
        .start(&fixture.actor, "TestFile", "文件输出", move |_| {
            Ok(TaskOutput::file(
                "测试.pdf".into(),
                "application/pdf",
                output,
            ))
        })
        .unwrap();
    let id = started["jobId"].as_str().unwrap();
    let finished = wait(&jobs, &fixture.actor, id);
    assert_eq!(finished["status"], "Succeeded");
    export_doc_contracts::validation::response(GET_JOB.id, &finished).unwrap();
    jobs.close().unwrap();
    drop(jobs);
    drop(store);
    let (store, jobs) = fixture.open();
    let file = jobs.download(&fixture.actor, id).unwrap();
    assert_eq!(file.file_name, "测试.pdf");
    assert_eq!(file.content, bytes);
    let other = Actor {
        id: 2,
        ..fixture.actor.clone()
    };
    assert_eq!(jobs.get(&other, id).unwrap_err().status, Some(403));
    assert_eq!(jobs.list(&other, &[]).unwrap()["totalCount"], 0);
    jobs.operation(DELETE_JOB, &fixture.actor, id).unwrap();
    jobs.operation(DELETE_JOB, &fixture.actor, id).unwrap();
    assert_eq!(jobs.get(&fixture.actor, id).unwrap_err().status, Some(404));
    assert!(store.all("background-jobs").unwrap().is_empty());
}

#[test]
fn restart_marks_interrupted_jobs_failed_without_repeating_work() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let interrupted =
        persistence::create(&store, &fixture.actor, "Delivery", "待完成操作").unwrap();
    drop(jobs);
    drop(store);
    let (_store, jobs) = fixture.open();
    let recovered = jobs
        .get(&fixture.actor, interrupted["jobId"].as_str().unwrap())
        .unwrap();
    assert_eq!(recovered["status"], "Failed");
    assert_eq!(recovered["canCancel"], false);
    assert!(
        recovered["errorMessage"]
            .as_str()
            .unwrap()
            .contains("未自动重复")
    );
}

#[test]
fn cancellation_wins_over_in_flight_output_and_completed_tasks_are_unchanged() {
    let fixture = Fixture::new();
    let (_store, jobs) = fixture.open();
    let (entered_tx, entered_rx) = mpsc::channel();
    let (continue_tx, continue_rx) = mpsc::channel();
    let started = jobs
        .start(&fixture.actor, "TestFile", "取消", move |_| {
            entered_tx.send(()).unwrap();
            continue_rx.recv_timeout(Duration::from_secs(3)).unwrap();
            Ok(TaskOutput::file(
                "取消.pdf".into(),
                "application/pdf",
                b"%PDF-output".to_vec(),
            ))
        })
        .unwrap();
    let id = started["jobId"].as_str().unwrap();
    entered_rx.recv_timeout(Duration::from_secs(3)).unwrap();
    assert_eq!(
        jobs.operation(DELETE_JOB, &fixture.actor, id)
            .unwrap_err()
            .status,
        Some(409)
    );
    jobs.operation(CANCEL_JOB, &fixture.actor, id).unwrap();
    continue_tx.send(()).unwrap();
    assert_eq!(wait(&jobs, &fixture.actor, id)["status"], "Canceled");
    assert!(jobs.download(&fixture.actor, id).is_err());
    jobs.operation(CANCEL_JOB, &fixture.actor, id).unwrap();
    assert_eq!(jobs.get(&fixture.actor, id).unwrap()["status"], "Canceled");
    assert_eq!(
        jobs.clear_finished(&fixture.actor).unwrap()["affectedCount"],
        1
    );
}

#[test]
fn malformed_output_fails_without_publishing_or_panicking() {
    let fixture = Fixture::new();
    let (_store, jobs) = fixture.open();
    let started = jobs
        .start(&fixture.actor, "TestFile", "无效输出", move |_| {
            Ok(TaskOutput::file(
                "../escape.pdf".into(),
                "application/pdf",
                vec![1],
            ))
        })
        .unwrap();
    let id = started["jobId"].as_str().unwrap();
    assert_eq!(wait(&jobs, &fixture.actor, id)["status"], "Failed");
    assert!(jobs.download(&fixture.actor, id).is_err());
}

#[test]
fn queries_filter_status_and_text_without_disclosing_another_users_jobs() {
    let fixture = Fixture::new();
    let (_store, jobs) = fixture.open();
    jobs.completed(&fixture.actor, "Export", "发票任务", "导出完成")
        .unwrap();
    let started = jobs
        .start(&fixture.actor, "Export", "错误任务", |_| {
            Err(unavailable("字体缺失"))
        })
        .unwrap();
    wait(&jobs, &fixture.actor, started["jobId"].as_str().unwrap());
    let page = jobs
        .list(
            &fixture.actor,
            &[("status", "failed".into()), ("keyword", "字体".into())],
        )
        .unwrap();
    assert_eq!(page["totalCount"], 1);
    assert_eq!(page["items"][0]["jobId"], started["jobId"]);
    assert_eq!(
        jobs.list(
            &Actor {
                id: 2,
                ..fixture.actor.clone()
            },
            &[]
        )
        .unwrap()["totalCount"],
        0
    );
    assert_eq!(
        jobs.list(&fixture.actor, &[("status", "unknown".into())])
            .unwrap_err()
            .status,
        Some(400)
    );
}

#[test]
fn retention_keeps_running_work_and_removes_terminal_inputs_and_outputs_atomically() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let pending = persistence::create(&store, &fixture.actor, "Export", "继续执行").unwrap();
    let mut records = vec![];
    for day in 0..5 {
        let replay = retry::Replay::new(
            UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB,
            &[],
            &json!({}),
        )
        .with_input("原始.xlsx", b"retained-input");
        let job = persistence::create_replayable(
            &store,
            &fixture.actor,
            "Export",
            "文件输出",
            Some(&replay),
        )
        .unwrap();
        let id = job["jobId"].as_str().unwrap();
        persistence::finish(
            &store,
            &fixture.actor,
            id,
            Ok(TaskOutput::file(
                "输出.xlsx".into(),
                "application/octet-stream",
                vec![1, 2, 3],
            )),
        )
        .unwrap();
        let mut record =
            persistence::checked(&*store.connection().unwrap(), &fixture.actor, id).unwrap();
        record["completedAt"] =
            json!((chrono::Utc::now() - chrono::Duration::days(day)).to_rfc3339());
        let record_id = record["id"].as_i64().unwrap();
        store
            .connection()
            .unwrap()
            .set_body(record_id, &record)
            .unwrap();
        records.push((id.to_owned(), record_id));
    }
    assert_eq!(
        retention::prune(
            &store,
            retention::Retention {
                days: 2,
                global_limit: 3,
                per_user_limit: 1
            },
            chrono::Utc::now()
        )
        .unwrap(),
        4
    );
    assert_eq!(
        jobs.get(&fixture.actor, pending["jobId"].as_str().unwrap())
            .unwrap()["status"],
        "Running"
    );
    assert!(jobs.download(&fixture.actor, &records[0].0).is_ok());
    for (id, record_id) in &records[1..] {
        assert_eq!(jobs.get(&fixture.actor, id).unwrap_err().status, Some(404));
        let tx = store.connection().unwrap();
        assert!(tx.blob(*record_id, "job-output").unwrap().is_none());
        assert!(tx.blob(*record_id, "job-input").unwrap().is_none());
    }
    assert_eq!(
        retention::prune(
            &store,
            retention::Retention {
                days: 2,
                global_limit: 3,
                per_user_limit: 1
            },
            chrono::Utc::now()
        )
        .unwrap(),
        0
    );
    assert!(retention::Retention::from_lookup(|_| Some("0".into())).is_err());
}

#[cfg(feature = "excel")]
#[test]
fn retry_survives_restart_reauthorizes_and_keeps_private_requests_off_the_wire() {
    use crate::engine::NativeService;
    let fixture = Fixture::new();
    let template_root = fixture.root.join("Resources/ExcelTemplates");
    fs::create_dir_all(&template_root).unwrap();
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_owned();
    fs::copy(
        workspace.join("Resources/ExcelTemplates/invoice-import-template.xlsx"),
        template_root.join("invoice-import-template.xlsx"),
    )
    .unwrap();
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    let login: Value = serde_json::from_slice(
        &service
            .dispatch(
                LOGIN,
                &[],
                &[],
                Some(json!({"username":"admin","password":""})),
                "",
            )
            .unwrap(),
    )
    .unwrap();
    let actor = service
        .sessions
        .actor(&service.store, login["accessToken"].as_str().unwrap())
        .unwrap();
    let started = service
        .jobs
        .start_replayable(
            &actor,
            "ExcelTemplateExport",
            "Excel 模板",
            Some(retry::Replay::new(
                START_EXCEL_TEMPLATE_DOWNLOAD_JOB,
                &[],
                &json!({}),
            )),
            |_| Err(unavailable("模拟可恢复的任务失败")),
        )
        .unwrap();
    let id = started["jobId"].as_str().unwrap().to_owned();
    let failed = wait(&service.jobs, &actor, &id);
    assert_eq!(failed["canRetry"], true);
    assert_eq!(failed["retryOperation"], "StartExcelTemplateExportJob");
    assert_eq!(failed["retryRequestJson"], "");
    assert!(failed.get("_retry").is_none());
    service.close().unwrap();
    drop(service);
    let service = NativeService::open(fixture.paths.clone()).unwrap();
    assert_eq!(
        retry::execute(
            &service,
            &Actor {
                id: actor.id + 100,
                admin: false,
                ..actor.clone()
            },
            &id
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    assert_eq!(
        retry::execute(
            &service,
            &Actor {
                admin: false,
                grants: vec![],
                ..actor.clone()
            },
            &id
        )
        .unwrap_err()
        .status,
        Some(403)
    );
    let retried = retry::execute(&service, &actor, &id).unwrap();
    let new_id = retried["jobId"].as_str().unwrap();
    assert_ne!(new_id, id);
    assert_eq!(wait(&service.jobs, &actor, new_id)["status"], "Succeeded");
    assert!(
        service
            .jobs
            .download(&actor, new_id)
            .unwrap()
            .content
            .starts_with(b"PK")
    );
    assert_eq!(service.jobs.get(&actor, &id).unwrap()["status"], "Failed");
    assert_eq!(
        retry::execute(&service, &actor, new_id).unwrap_err().status,
        Some(409)
    );
    service.close().unwrap();
}
