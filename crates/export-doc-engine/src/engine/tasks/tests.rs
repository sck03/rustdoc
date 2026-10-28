use super::*;
use crate::contracts;
use crate::paths::{RuntimePaths, nonce};
use std::{fs, path::PathBuf, sync::mpsc, time::Instant};
mod completion;
mod publication;

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
                edition: Default::default(),
                id: 1,
                name: "任务用户".into(),
                company: "DEFAULT".into(),
                department: "GENERAL".into(),
                admin: false,
                grants: vec![],
                departments: vec![],
                oa_assigned_only: false,
            },
        }
    }
    fn open(&self) -> (Arc<Store>, Jobs) {
        let store = self.store();
        let jobs = Jobs::open(store.clone()).unwrap();
        (store, jobs)
    }
    fn store(&self) -> Arc<Store> {
        let store = Arc::new(Store::open(&self.paths).unwrap());
        crate::engine::auth::seed(&store).unwrap();
        store.transaction(|tx| {
            if tx.find_identity("users", "task-other")?.is_none() {
                let value = contracts::overlay(contracts::initial(contracts::schema("ApiUserAccountDto")),
                    &json!({"username":"task-other","fullName":"另一测试用户","role":"Admin","isActive":true,"companyScope":"DEFAULT","departmentId":"GENERAL"}));
                crate::engine::store::save(tx, "users", 0, value, Some("task-other".into()), &self.actor, "create")?;
            }
            Ok(())
        }).unwrap();
        store
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
        .start(
            &fixture.actor,
            GET_JOB,
            "TestFile",
            "文件输出",
            move |_| {
                Ok(TaskOutput::file(
                    "测试.pdf".into(),
                    "application/pdf",
                    output,
                ))
            },
        )
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
        edition: Default::default(),
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
        .start(&fixture.actor, GET_JOB, "TestFile", "取消", move |_| {
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
        .start(
            &fixture.actor,
            GET_JOB,
            "TestFile",
            "无效输出",
            move |_| {
                Ok(TaskOutput::file(
                    "../escape.pdf".into(),
                    "application/pdf",
                    vec![1],
                ))
            },
        )
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
        .start(&fixture.actor, GET_JOB, "Export", "错误任务", |_| {
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
                edition: Default::default(),
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
#[cfg(feature = "excel")]
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
            GET_JOB,
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
                per_user_limit: 1,
                ..Default::default()
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
                per_user_limit: 1,
                ..Default::default()
            },
            chrono::Utc::now()
        )
        .unwrap(),
        0
    );
    assert!(retention::Retention::from_lookup(|_| Some("0".into())).is_err());
}

#[test]
fn output_byte_retention_preserves_history_and_is_idempotent() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let mut ids = vec![];
    for _ in 0..3 {
        let job = persistence::create(&store, &fixture.actor, "Capacity", "容量结果").unwrap();
        let id = job["jobId"].as_str().unwrap();
        persistence::finish(
            &store,
            &fixture.actor,
            id,
            Ok(TaskOutput::file(
                "result.txt".into(),
                "text/plain",
                b"123456".to_vec(),
            )),
        )
        .unwrap();
        ids.push(id.to_owned());
    }
    let policy = retention::Retention {
        global_output_bytes: 10,
        per_user_output_bytes: 10,
        ..Default::default()
    };
    assert_eq!(
        retention::prune(&store, policy, chrono::Utc::now()).unwrap(),
        0
    );
    for id in &ids[..2] {
        let snapshot = jobs.get(&fixture.actor, id).unwrap();
        assert_eq!(snapshot["status"], "Succeeded");
        assert_eq!(snapshot["outputPath"], "");
        assert!(snapshot["detailText"].as_str().unwrap().contains("回收"));
        assert_eq!(
            jobs.download(&fixture.actor, id).err().unwrap().status,
            Some(409)
        );
    }
    assert_eq!(
        jobs.download(&fixture.actor, &ids[2]).unwrap().content,
        b"123456"
    );
    assert_eq!(jobs.metrics().unwrap()["retainedOutputBytes"], 6);
    assert_eq!(
        retention::prune(&store, policy, chrono::Utc::now()).unwrap(),
        0
    );
    jobs.close().unwrap();
}

#[cfg(feature = "excel")]
#[test]
fn retry_inputs_are_released_on_success_and_bounded_after_failure_or_cancellation() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let mut ids = vec![];
    for state in ["Succeeded", "Failed", "Canceled", "Failed"] {
        let replay = retry::Replay::new(
            UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB,
            &[],
            &json!({}),
        )
        .with_input("input.xlsx", b"123456");
        let started = persistence::create_replayable(
            &store,
            &fixture.actor,
            GET_JOB,
            "BookingSheetConvert",
            "输入保留",
            Some(&replay),
        )
        .unwrap();
        let id = started["jobId"].as_str().unwrap();
        if state == "Canceled" {
            persistence::cancel(&store, &fixture.actor, id).unwrap();
        }
        let result = if state == "Succeeded" {
            Ok(TaskOutput::file(
                "result.txt".into(),
                "text/plain",
                b"result".to_vec(),
            ))
        } else {
            Err(unavailable("模拟失败"))
        };
        persistence::finish(&store, &fixture.actor, id, result).unwrap();
        let row = persistence::checked(&*store.connection().unwrap(), &fixture.actor, id).unwrap();
        let record_id = row["id"].as_i64().unwrap();
        assert_eq!(
            store
                .connection()
                .unwrap()
                .blob(record_id, "job-input")
                .unwrap()
                .is_some(),
            state != "Succeeded"
        );
        ids.push((id.to_owned(), record_id));
    }
    assert_eq!(store.connection().unwrap().job_input_bytes().unwrap(), 18);
    let policy = retention::Retention {
        global_output_bytes: 12,
        per_user_output_bytes: 12,
        ..Default::default()
    };
    retention::prune(&store, policy, chrono::Utc::now()).unwrap();
    assert_eq!(store.connection().unwrap().job_input_bytes().unwrap(), 12);
    let expired =
        persistence::checked(&*store.connection().unwrap(), &fixture.actor, &ids[1].0).unwrap();
    assert_eq!(expired["status"], "Failed");
    assert_eq!(expired["_retryInput"], false);
    assert_eq!(
        jobs.get(&fixture.actor, &ids[1].0).unwrap()["canRetry"],
        false
    );
    assert!(expired["detailText"].as_str().unwrap().contains("重试输入"));
    assert!(
        store
            .connection()
            .unwrap()
            .blob(ids[2].1, "job-input")
            .unwrap()
            .is_some()
    );
    assert!(
        store
            .connection()
            .unwrap()
            .blob(ids[3].1, "job-input")
            .unwrap()
            .is_some()
    );
    retention::prune(&store, policy, chrono::Utc::now()).unwrap();
    assert_eq!(store.connection().unwrap().job_input_bytes().unwrap(), 12);
    jobs.close().unwrap();
}

#[test]
fn registered_document_retries_reach_their_original_use_case() {
    let fixture = Fixture::new();
    let service = crate::engine::NativeService::open(fixture.paths.clone()).unwrap();
    let admin_id = service.store.all("users").unwrap()[0]["id"]
        .as_i64()
        .unwrap();
    let actor = crate::engine::auth::current_actor(&service.store, admin_id).unwrap();
    let body = json!({"items":[{"reportType":"ExportDocument","templatePath":export_doc_report::Builtin::Invoice.path(),"name":"发票","withSeal":false}],"createZip":true});
    let parameters = [("invoiceId", "999999999".into())];
    for operation in [
        START_INVOICE_DOCUMENT_PACKAGE_DOWNLOAD_JOB,
        START_INVOICE_DOCUMENT_PACKAGE_SAVE_TO_PATH_JOB,
        #[cfg(feature = "mail")]
        START_INVOICE_DOCUMENT_EMAIL_JOB,
    ] {
        let replay = retry::Replay::new(operation, &parameters, &body);
        let started = persistence::create_replayable(
            &service.store,
            &actor,
            GET_JOB,
            "DocumentRetry",
            "重试分派",
            Some(&replay),
        )
        .unwrap();
        let id = started["jobId"].as_str().unwrap();
        persistence::finish(
            &service.store,
            &actor,
            id,
            Err(unavailable("模拟输出前失败")),
        )
        .unwrap();
        assert_eq!(service.jobs.get(&actor, id).unwrap()["canRetry"], true);
        assert_eq!(
            retry::execute(&service, &actor, id).unwrap_err().status,
            Some(404),
            "retry must reach the source invoice check for {}",
            operation.id
        );
    }
    service.close().unwrap();
}

#[cfg(feature = "excel")]
#[test]
fn reclaimed_retry_input_is_a_conflict_inside_the_read_snapshot() {
    let fixture = Fixture::new();
    let service = crate::engine::NativeService::open(fixture.paths.clone()).unwrap();
    let admin_id = service.store.all("users").unwrap()[0]["id"]
        .as_i64()
        .unwrap();
    let actor = crate::engine::auth::current_actor(&service.store, admin_id).unwrap();
    let replay = retry::Replay::new(
        UPLOAD_AND_START_BOOKING_SHEET_CONVERT_DOWNLOAD_JOB,
        &[],
        &json!({}),
    )
    .with_input("input.xlsx", b"workbook");
    let started = persistence::create_replayable(
        &service.store,
        &actor,
        GET_JOB,
        "BookingSheetConvert",
        "回收冲突",
        Some(&replay),
    )
    .unwrap();
    let id = started["jobId"].as_str().unwrap();
    persistence::finish(&service.store, &actor, id, Err(unavailable("模拟失败"))).unwrap();
    retention::prune(
        &service.store,
        retention::Retention {
            global_output_bytes: 1,
            per_user_output_bytes: 1,
            ..Default::default()
        },
        chrono::Utc::now(),
    )
    .unwrap();
    assert_eq!(service.jobs.get(&actor, id).unwrap()["canRetry"], false);
    assert_eq!(
        retry::execute(&service, &actor, id).unwrap_err().status,
        Some(409)
    );
    assert_eq!(
        service
            .store
            .connection()
            .unwrap()
            .job_input_bytes()
            .unwrap(),
        0
    );
    service.close().unwrap();
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
    let other_id = service.store.transaction(|tx| {
        let account = crate::contracts::overlay(crate::contracts::initial(crate::contracts::schema("ApiUserAccountDto")), &json!({"username":"other-task-owner","fullName":"其他任务账号","role":"User","isActive":true,"companyScope":"DEFAULT","departmentId":"GENERAL"}));
        let saved = crate::engine::store::save(tx, "users", 0, account, Some("other-task-owner".into()), &actor, "create")?;
        Ok(saved["id"].as_i64().unwrap())
    }).unwrap();
    let started = service
        .jobs
        .start_replayable(
            &actor,
            START_EXCEL_TEMPLATE_DOWNLOAD_JOB,
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
                edition: Default::default(),
                id: other_id,
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
                departments: vec![],
                oa_assigned_only: false,
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
