//! Manual, isolated comparisons. Historical algorithms never enter production builds.
#[path = "approval_fixture.rs"]
mod fixture;
use crate::{
    engine::{self, NativeService, oa, store},
    generated_api::*,
    paths::RuntimePaths,
};
use serde_json::{Value, json};
use std::{sync::mpsc, time::Instant};

#[test]
#[ignore = "manual query/lock/memory measurement; run test-native-postgres.ps1 -Scope QueryPerformance"]
fn sqlite_query_costs() {
    exercise(false);
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires a fresh isolated PostgreSQL 18 database and manual measurement"]
fn team_query_costs() {
    exercise(true);
}

fn exercise(postgres: bool) {
    let mode = std::env::var("EXPORTDOC_QUERY_BENCH_MODE").expect("explicit baseline/current mode");
    assert!(matches!(mode.as_str(), "baseline" | "current"));
    let root = engine::test_support::ScratchDirectory::new();
    let paths = RuntimePaths::server(&root.0, &root.0.join("Data")).unwrap();
    let bootstrap = crate::paths::nonce().unwrap();
    let service = if postgres {
        #[cfg(feature = "postgres")]
        {
            let maintenance = std::env::var("EXPORTDOC_TEST_ENGINE_MAINTENANCE").unwrap();
            let app = std::env::var("EXPORTDOC_TEST_ENGINE_APP").unwrap();
            export_doc_storage::Connection::initialize_postgres(&maintenance, "native_owner")
                .unwrap();
            NativeService::open_postgres(paths, &app, bootstrap.clone(), Default::default())
                .unwrap()
        }
        #[cfg(not(feature = "postgres"))]
        panic!("PostgreSQL feature required")
    } else {
        NativeService::open(paths).unwrap()
    };
    let login: Value = serde_json::from_slice(&service.dispatch_with_bootstrap(
        LOGIN, &[], &[], Some(json!({"username":"admin","password":if postgres {"Query-Measure-2026"} else {""}})), "", &bootstrap,
    ).unwrap()).unwrap();
    let token = login["accessToken"].as_str().unwrap();
    let admin = service.sessions.actor(&service.store, token).unwrap();
    let (reviewer_id, reviewer_token) = fixture::reviewer(&service, token);
    let applicant = fixture::request(
        &service,
        token,
        CREATE_PERSONNEL,
        Some(json!({
            "requestKey":crate::paths::nonce().unwrap(), "employeeNumber":"QUERY-APPLICANT",
            "departmentId":"GENERAL", "jobTitle":"隔离验收", "employmentType":"FullTime",
            "hireDate":"2026-01-01", "profile":{"fullName":"隔离申请人"}
        })),
    );
    let mut settings = fixture::request(&service, token, GET_OA_APPROVAL_SETTINGS, None);
    settings["rules"]
        .as_array_mut()
        .unwrap()
        .iter_mut()
        .find(|r| r["key"] == "general")
        .unwrap()["stages"] = json!([fixture::node(
        "measured-review",
        &[],
        "Named",
        json!([reviewer_id])
    )]);
    let saved = fixture::save_settings(&service, token, settings).unwrap();
    fixture::request(
        &service,
        token,
        PUBLISH_OA_DEFINITION,
        Some(json!({"expectedVersion":saved["versionNumber"],"definitionKey":"general"})),
    );
    let draft = fixture::request(
        &service,
        token,
        CREATE_GENERAL_REQUEST,
        Some(json!({
            "requestKey":crate::paths::nonce().unwrap(),"employeeId":applicant["employee"]["id"],
            "title":"查询成本验收","reason":"匿名隔离数据","category":"Other"
        })),
    );
    let request_id = draft["id"].as_i64().unwrap();
    fixture::call(&service, token, SUBMIT_GENERAL_REQUEST, request_id, Some(json!({
        "expectedVersion":draft["versionNumber"],"commandKey":crate::paths::nonce().unwrap(),"note":"提交"
    }))).unwrap();
    let mut reviewer = service
        .sessions
        .actor(&service.store, &reviewer_token)
        .unwrap();
    // Force the actual participation-only path used by delegated OA identities.
    reviewer.oa_assigned_only = true;
    let row = service.store.get("oa-general", request_id).unwrap();
    // Stop schedulers so unrelated background reads cannot contaminate counters.
    service.workflow_scheduler.stop().unwrap();
    service.backup_scheduler.stop().unwrap();
    #[cfg(feature = "exchange-rates")]
    service.exchange_scheduler.stop().unwrap();
    let mut review_samples = vec![];
    for _ in 0..5 {
        let before = service.store.metrics();
        let start = Instant::now();
        service
            .store
            .read(|tx| {
                for _ in 0..100 {
                    let keys = if mode == "baseline" {
                        assert!(oa::viewable(tx, &reviewer, "office.general", &row)?);
                        oa::approval::ReviewSnapshot::read(tx, &reviewer, &row)?.task_keys()
                    } else {
                        let mut access = oa::Visibility::new(tx, &reviewer, "office.general", &row);
                        assert!(access.viewable()?);
                        access.review()?.task_keys()
                    };
                    assert_eq!(keys.len(), 1);
                }
                Ok(())
            })
            .unwrap();
        review_samples.push(measure(&before, &service.store.metrics(), start));
    }
    let store = &service.store;
    store.transaction(|tx| {
        for kind in ["sw-coo", "sw-acd", "sw-batches"] {
            for index in 0..3000 {
                let mut body = json!({"sourceId":if index < 205 {7} else {8},"versionNumber":1,
                    "ownerUserId":admin.id,"companyScope":"DEFAULT","departmentId":"GENERAL","payload":"x".repeat(8192)});
                let id = tx.insert(&export_doc_storage::RecordWrite::unkeyed(kind, &body))?;
                body["id"] = json!(id);
                tx.set_body(id, &body)?;
            }
        }
        Ok(())
    }).unwrap();
    let plan = store
        .connection()
        .unwrap()
        .explain_query(&export_doc_storage::QueryPlan::Generic(
            &export_doc_storage::GenericQuery {
                kind: "sw-coo",
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                filters: &[("sourceId", "7".into())],
                fields: &[("id", "id"), ("versionNumber", "versionNumber")],
                limit: 200,
                ..Default::default()
            },
        ))
        .unwrap();
    let mut purge_samples = vec![];
    for _ in 0..5 {
        let before = store.metrics();
        let start = Instant::now();
        let mut read_calls = 0;
        std::thread::scope(|threads| {
            let (ready, waiting) = mpsc::channel();
            let waiter = threads.spawn(move || {
                waiting.recv().unwrap();
                store.transaction(|_| Ok(())).unwrap();
            });
            store
                .preview_as(&admin, |tx, _| {
                    ready.send(()).unwrap();
                    let before_scan = store.metrics()["operations"]["completed"].as_u64().unwrap();
                    let result = if mode == "baseline" {
                        // c3639787: read complete kinds while holding the same write coordinator.
                        for kind in ["sw-coo", "sw-acd", "sw-batches"] {
                            for row in store::all(tx, kind)?
                                .into_iter()
                                .filter(|r| r["sourceId"] == 7)
                            {
                                assert!(tx.delete(kind, row["id"].as_i64().unwrap(), 1)?);
                            }
                        }
                        Ok(())
                    } else {
                        super::delete_workspace(tx, 7)
                    };
                    // One observed adapter delete per linked record; the remainder are reads.
                    read_calls = store.metrics()["operations"]["completed"].as_u64().unwrap()
                        - before_scan
                        - 3 * 205;
                    result
                })
                .unwrap();
            waiter.join().unwrap();
        });
        let mut sample = measure(&before, &store.metrics(), start);
        sample["workspaceReadCalls"] = json!(read_calls);
        purge_samples.push(sample);
    }
    let memory = if cfg!(windows) {
        let output = std::process::Command::new("pwsh").args(["-NoProfile", "-Command",
            "Get-Process -Id $env:QUERY_MEASURE_PID | Select-Object WorkingSet64,PeakWorkingSet64 | ConvertTo-Json -Compress"])
            .env("QUERY_MEASURE_PID", std::process::id().to_string()).output().unwrap();
        assert!(output.status.success());
        serde_json::from_slice::<Value>(&output.stdout).unwrap()
    } else {
        Value::Null
    };
    println!(
        "QUERY_PERFORMANCE {}",
        json!({"database":if postgres {"PostgreSQL18"} else {"SQLite"},"mode":mode,
        "recordsPerKind":3000,"linkedPerKind":205,"bodyPayloadBytes":8192,"reviewRowsPerRound":100,
        "queryCountUnit":"storage adapter calls; includes reads and writes, not individual SQL statements",
        "review":review_samples,"purgeWithWaitingWriter":purge_samples,"processMemoryBytes":memory,"sourceQueryPlan":plan})
    );
    service.close().unwrap();
}
fn measure(before: &Value, after: &Value, start: Instant) -> Value {
    let delta = |group: &str, key: &str| {
        after[group][key].as_u64().unwrap() - before[group][key].as_u64().unwrap()
    };
    json!({"responseMs":start.elapsed().as_secs_f64()*1000.0,
        "storageCalls":delta("operations","completed"),"storageMicroseconds":delta("operations","totalMicroseconds"),
        "writeWaitMicroseconds":delta("writeWait","totalMicroseconds"),"poolWaitMicroseconds":delta("wait","totalMicroseconds")})
}
