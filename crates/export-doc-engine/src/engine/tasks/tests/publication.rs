use super::*;
use crate::engine::store;

fn file_output(path: &std::path::Path, bytes: &[u8]) -> TaskOutput {
    let mut output = TaskOutput::file("suggested.pdf".into(), "application/pdf", bytes.to_vec());
    output.destination = Some(path.into());
    output
}

fn directory_output(path: &std::path::Path, files: &[(&str, &[u8])]) -> TaskOutput {
    TaskOutput {
        file: None,
        managed_file: None,
        destination: None,
        publication: None,
        archive: None,
        detail: "saved folder".into(),
        directory: Some(DirectoryOutput {
            path: path.into(),
            files: files
                .iter()
                .map(|(name, bytes)| FileOutput {
                    file_name: (*name).into(),
                    media_type: "application/pdf".into(),
                    content: bytes.to_vec(),
                })
                .collect(),
        }),
    }
}

#[test]
fn streamed_publication_recovers_large_files_and_preserves_prior_output() {
    use std::io::{Seek, SeekFrom, Write};
    let fixture = Fixture::new();
    let store = fixture.store();
    let source = fixture.root.0.join("source.edmrecovery");
    let mut file = fs::File::create(&source).unwrap();
    file.seek(SeekFrom::Start(65 * 1024 * 1024)).unwrap();
    file.write_all(b"large verified archive").unwrap();
    drop(file);
    let target = fixture.root.0.join("selected.edmrecovery");
    fs::write(&target, b"prior archive").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Backup", "流式保存恢复").unwrap();
    let mut result = TaskOutput::managed_file(source, "saved".into()).unwrap();
    result.destination = Some(target.clone());
    let published = output::publish(&store, job["jobId"].as_str().unwrap(), &result).unwrap();
    assert_eq!(
        fs::metadata(&target).unwrap().len(),
        result.managed_file.as_ref().unwrap().size
    );
    drop(published);
    output::recover(&store).unwrap();
    assert_eq!(fs::read(&target).unwrap(), b"prior archive");
}

fn break_version(store: &Store, actor: &Actor, id: &str) -> i64 {
    store
        .transaction(|tx| {
            let mut record = persistence::checked(tx, actor, id)?;
            let record_id = record["id"].as_i64().unwrap();
            record["versionNumber"] = json!(100000);
            tx.set_body(record_id, &record)?;
            Ok(record_id)
        })
        .unwrap()
}

fn change_user(
    tx: &store::Connection<'_>,
    actor: &Actor,
    id: i64,
    field: &str,
    value: Value,
) -> Result<()> {
    let mut user = store::get(tx, "users", id)?;
    let identity = user["username"].as_str().unwrap().to_owned();
    user[field] = value;
    store::save(tx, "users", id, user, Some(identity), actor, "restrict")?;
    Ok(())
}

#[cfg(any(feature = "postgres", feature = "excel"))]
fn save_invoice(store: &Store, actor: &Actor, number: &str) -> i64 {
    let invoice = crate::invoice::InvoiceDraft::demo("2026-10-28", number)
        .build()
        .unwrap();
    let body = serde_json::to_value(invoice).unwrap();
    store
        .transaction(|tx| store::save(tx, "invoices", 0, body, None, actor, "create"))
        .unwrap()["id"]
        .as_i64()
        .unwrap()
}

#[test]
fn completed_download_rechecks_originating_module_after_revocation() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let admin = crate::engine::auth::current_actor(&store, 1).unwrap();
    let account = crate::engine::accounts::save(
        &store,
        &admin,
        0,
        json!({
            "username":"download-review", "fullName":"下载权限验收", "role":"OfficeEmployee",
            "companyScope":"DEFAULT", "departmentId":"GENERAL", "isActive":true,
            "resetPassword":"Download-Review-2026", "permissionGrants":[
                {"resourceKey":"document.jobs","action":"view","dataScope":"own"},
                {"resourceKey":"office.general","action":"view","dataScope":"own"}
            ]
        }),
    )
    .unwrap();
    let actor = crate::engine::auth::current_actor(&store, account["user"]["id"].as_i64().unwrap())
        .unwrap();
    let job = persistence::create_replayable(
        &store,
        &actor,
        GET_GENERAL_REQUEST,
        "Export",
        "原始业务授权",
        None,
    )
    .unwrap();
    let id = job["jobId"].as_str().unwrap();
    persistence::finish(
        &store,
        &actor,
        id,
        Ok(TaskOutput::file(
            "review.pdf".into(),
            "application/pdf",
            b"%PDF-review".to_vec(),
        )),
    )
    .unwrap();
    assert_eq!(jobs.download(&actor, id).unwrap().content, b"%PDF-review");
    let (pending, snapshot) = jobs.capture_download(&actor, id).unwrap();
    store
        .transaction(|tx| {
            change_user(
                tx,
                &admin,
                actor.id,
                "disabledModules",
                json!(["office.general"]),
            )
        })
        .unwrap();
    drop(pending.verify().unwrap());
    assert_eq!(
        jobs.authorize_download(&actor, id, Some(&snapshot))
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(jobs.download(&actor, id).err().unwrap().status, Some(403));
    assert_eq!(
        jobs.authorize_download(&actor, id, None)
            .unwrap_err()
            .status,
        Some(403)
    );
    assert_eq!(
        jobs.get(&actor, id).unwrap()["status"],
        "Succeeded",
        "revocation does not rewrite history"
    );
}

#[test]
fn business_archive_is_atomic_cancelable_and_survives_task_cleanup() {
    for scenario in ["cancel", "denied", "rollback", "ready"] {
        let fixture = Fixture::new();
        let store = fixture.store();
        let body = json!({"status":"Approved"});
        let record = store
            .transaction(|tx| {
                store::save(
                    tx,
                    "archive-fixture",
                    0,
                    body,
                    None,
                    &fixture.actor,
                    "submit",
                )
            })
            .unwrap();
        let record_id = record["id"].as_i64().unwrap();
        let job = persistence::create(&store, &fixture.actor, "Export", "归档事务").unwrap();
        let id = job["jobId"].as_str().unwrap();
        let mut result = TaskOutput::file(
            "approval.pdf".into(),
            "application/pdf",
            b"%PDF-archive".to_vec(),
        );
        result.archive = Some(ArchiveOutput {
            record_id,
            kind: "oa-approval-pdf",
            file: result.file.take().unwrap(),
        });
        if scenario == "cancel" {
            persistence::cancel(&store, &fixture.actor, id).unwrap();
        }
        if scenario == "denied" {
            result.publication = Some(PublicationCheck::new(|_, _| {
                Err(error(403, "申请访问权限已撤销。"))
            }));
        }
        if scenario == "rollback" {
            break_version(&store, &fixture.actor, id);
        }
        let finished = persistence::finish(&store, &fixture.actor, id, Ok(result));
        if scenario == "rollback" {
            assert!(finished.is_err());
        } else {
            finished.unwrap();
        }
        let blob = store
            .connection()
            .unwrap()
            .file_info(record_id, "oa-approval-pdf")
            .unwrap();
        if scenario == "ready" {
            assert_eq!(
                store.files.load(&blob.unwrap(), 1024).unwrap().content,
                b"%PDF-archive"
            );
            persistence::delete(&store, &fixture.actor, id).unwrap();
            persistence::recover(&store).unwrap();
            assert_eq!(
                store
                    .files
                    .load(
                        &store
                            .connection()
                            .unwrap()
                            .file_info(record_id, "oa-approval-pdf")
                            .unwrap()
                            .unwrap(),
                        1024
                    )
                    .unwrap()
                    .content,
                b"%PDF-archive"
            );
        } else {
            assert!(blob.is_none());
        }
    }
}

#[test]
fn saved_location_keeps_the_selected_name_and_survives_download_copy_expiry() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let target = fixture.root.0.join("custom-name.pdf");
    let job = persistence::create(&store, &fixture.actor, "Export", "保存到所选文件").unwrap();
    let id = job["jobId"].as_str().unwrap();
    persistence::finish(
        &store,
        &fixture.actor,
        id,
        Ok(file_output(&target, b"%PDF-1.7 test")),
    )
    .unwrap();
    assert_eq!(
        jobs.get(&fixture.actor, id).unwrap()["outputPath"],
        target.to_str().unwrap()
    );
    retention::prune(
        &store,
        retention::Retention {
            global_output_bytes: 1,
            per_user_output_bytes: 1,
            ..Default::default()
        },
        chrono::Utc::now(),
    )
    .unwrap();
    assert_eq!(
        jobs.get(&fixture.actor, id).unwrap()["outputPath"],
        target.to_str().unwrap()
    );
    assert_eq!(
        jobs.download(&fixture.actor, id).err().unwrap().status,
        Some(409)
    );
    assert!(target.is_file());
    persistence::delete(&store, &fixture.actor, id).unwrap();
    assert!(
        target.is_file(),
        "deleting task metadata must retain the user's file"
    );
}

#[test]
fn metadata_write_failure_restores_a_preexisting_selected_file() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("existing.pdf");
    fs::write(&target, b"original user output").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Export", "保存冲突").unwrap();
    let id = job["jobId"].as_str().unwrap();
    // Inject an optimistic SQL update failure after the filesystem write.
    let record_id = break_version(&store, &fixture.actor, id);
    assert!(
        persistence::finish(
            &store,
            &fixture.actor,
            id,
            Ok(file_output(&target, b"%PDF-1.7 replacement"))
        )
        .is_err()
    );
    assert_eq!(fs::read(&target).unwrap(), b"original user output");
    assert!(
        store
            .connection()
            .unwrap()
            .blob(record_id, "job-output")
            .unwrap()
            .is_none()
    );
}

#[test]
fn recovery_uses_the_database_receipt_to_restore_or_keep_a_published_file() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("selected.pdf");
    fs::write(&target, b"old").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Export", "中断输出").unwrap();
    let id = job["jobId"].as_str().unwrap();
    let pending =
        output::publish(&store, id, &file_output(&target, b"%PDF-1.7 uncommitted")).unwrap();
    drop(pending); // Simulate exit between filesystem publication and database commit.
    output::recover(&store).unwrap();
    assert_eq!(fs::read(&target).unwrap(), b"old");
    let pending =
        output::publish(&store, id, &file_output(&target, b"%PDF-1.7 committed")).unwrap();
    drop(pending);
    store
        .transaction(|tx| {
            let mut record = persistence::checked(tx, &fixture.actor, id)?;
            record["status"] = json!("Succeeded");
            store::save(
                tx,
                "background-jobs",
                record["id"].as_i64().unwrap(),
                record,
                Some(id.into()),
                &fixture.actor,
                "finish",
            )?;
            Ok(())
        })
        .unwrap();
    output::recover(&store).unwrap();
    assert_eq!(fs::read(&target).unwrap(), b"%PDF-1.7 committed");
    output::recover(&store).unwrap();
}

#[test]
fn directory_publication_records_its_location_and_keeps_unexpected_changes() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("selected-folder");
    let job = persistence::create(&store, &fixture.actor, "Export", "文件夹输出").unwrap();
    let id = job["jobId"].as_str().unwrap();
    let make = || directory_output(&target, &[("one.pdf", b"%PDF-1.7 test")]);
    let mut published = output::publish(&store, id, &make()).unwrap().unwrap();
    fs::write(target.join("user-note.txt"), b"keep user content").unwrap();
    assert!(published.rollback().is_err());
    assert_eq!(
        fs::read(target.join("user-note.txt")).unwrap(),
        b"keep user content"
    );
    fs::remove_file(target.join("user-note.txt")).unwrap();
    published.rollback().unwrap();
    persistence::finish(&store, &fixture.actor, id, Ok(make())).unwrap();
    let snapshot = persistence::get(&store, &fixture.actor, id).unwrap();
    assert_eq!(snapshot["outputPath"], target.to_str().unwrap());
    assert_eq!(
        fs::read_dir(&target).unwrap().count(),
        1,
        "transient ownership marker must be removed"
    );
}

#[test]
fn queued_finalization_rechecks_account_status_before_publishing() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("denied.pdf");
    let job = persistence::create(&store, &fixture.actor, "Export", "等待发布").unwrap();
    let id = job["jobId"].as_str().unwrap().to_owned();
    let (started, entered) = mpsc::channel();
    let mut worker = None;
    store
        .transaction(|tx| {
            let queued = store.clone();
            let actor = fixture.actor.clone();
            let id = id.clone();
            let target = target.clone();
            worker = Some(std::thread::spawn(move || {
                started.send(()).unwrap();
                persistence::finish(
                    &queued,
                    &actor,
                    &id,
                    Ok(file_output(&target, b"%PDF-1.7 denied")),
                )
            }));
            entered.recv_timeout(Duration::from_secs(2)).unwrap();
            change_user(
                tx,
                &fixture.actor,
                fixture.actor.id,
                "isActive",
                json!(false),
            )
        })
        .unwrap();
    worker.unwrap().join().unwrap().unwrap();
    assert!(!target.exists());
    let record = persistence::get(&store, &fixture.actor, &id).unwrap();
    assert_eq!(record["status"], "Failed");
    assert!(record["errorMessage"].as_str().unwrap().contains("停用"));
}

#[test]
fn recovery_finishes_partial_cleanup_and_compensation_ignores_request_cancellation() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("same.pdf");
    fs::write(&target, b"%PDF-identical").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Export", "恢复清理").unwrap();
    let id = job["jobId"].as_str().unwrap();
    let mut publication = output::publish(&store, id, &file_output(&target, b"%PDF-identical"))
        .unwrap()
        .unwrap();
    // The target already equals its snapshot; recovery need not read a snapshot
    // that an earlier cleanup removed before the process exited.
    let journal = fixture.paths.cache_root.join("TaskPublications").join(id);
    fs::remove_file(journal.join("previous.bin")).unwrap();
    let scope = crate::operation::OperationScope::new(Duration::from_secs(5));
    scope.cancel();
    scope.run(|| publication.rollback()).unwrap();
    assert_eq!(fs::read(&target).unwrap(), b"%PDF-identical");
    fs::create_dir(&journal).unwrap(); // Exit after removing the last manifest.
    output::recover(&store).unwrap();
    assert!(!journal.exists());
    let folder = fixture.root.0.join("partial-folder");
    let output = directory_output(
        &folder,
        &[("one.pdf", b"%PDF-one"), ("two.pdf", b"%PDF-two")],
    );
    let publication = output::publish(&store, id, &output).unwrap();
    drop(publication);
    fs::remove_file(folder.join("one.pdf")).unwrap(); // Interrupted verified rollback.
    output::recover(&store).unwrap();
    assert!(!folder.exists());
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires an isolated PostgreSQL 18 publication database"]
fn postgres_publication_recovery_snapshot_and_final_authorization_share_the_same_contract() {
    let maintenance = std::env::var("EXPORTDOC_TEST_PUBLICATION_MAINTENANCE").unwrap();
    let app = std::env::var("EXPORTDOC_TEST_PUBLICATION_APP").unwrap();
    export_doc_storage::Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let mut fixture = Fixture::new();
    let workspace = crate::engine::test_support::workspace_root();
    fixture.paths = RuntimePaths::server(&workspace, &fixture.root.0.join("Data")).unwrap();
    let store = Store::open_postgres(&fixture.paths, &app).unwrap();
    let bootstrap = crate::paths::nonce().unwrap();
    crate::engine::auth::bootstrap(
        &store,
        "admin",
        "Publication-Test-2026",
        &bootstrap,
        &bootstrap,
    )
    .unwrap();
    let actor = crate::engine::auth::current_actor(
        &store,
        store.all("users").unwrap()[0]["id"].as_i64().unwrap(),
    )
    .unwrap();
    let target = fixture.root.0.join("selected.pdf");
    fs::write(&target, b"previous user file").unwrap();
    let job = persistence::create(&store, &actor, "Export", "数据库失败恢复").unwrap();
    let id = job["jobId"].as_str().unwrap();
    break_version(&store, &actor, id);
    assert!(
        persistence::finish(
            &store,
            &actor,
            id,
            Ok(file_output(&target, b"%PDF-uncommitted"))
        )
        .is_err()
    );
    assert_eq!(fs::read(&target).unwrap(), b"previous user file");
    let job = persistence::create(&store, &actor, "Export", "重启恢复").unwrap();
    let id = job["jobId"].as_str().unwrap();
    drop(output::publish(&store, id, &file_output(&target, b"%PDF-interrupted")).unwrap());
    output::recover(&store).unwrap();
    assert_eq!(fs::read(&target).unwrap(), b"previous user file");

    let invoice_id = save_invoice(&store, &actor, "PG-SNAPSHOT");
    let requests = vec![
        json!({"reportType":"ExportDocument","templatePath":export_doc_report::Builtin::Invoice.path(),"withSeal":false}),
    ];
    let prepared = crate::engine::reports::prepare_batch(
        &store,
        &fixture.paths,
        &actor,
        invoice_id,
        &requests,
        false,
        "export-pdf",
    )
    .unwrap();
    store
        .transaction(|tx| {
            let mut record = store::get(tx, "invoices", invoice_id)?;
            record["invoiceNo"] = json!("PG-NEW");
            store::save(tx, "invoices", invoice_id, record, None, &actor, "edit")?;
            Ok(())
        })
        .unwrap();
    assert!(prepared[0].file_name(invoice_id).contains("PG-SNAPSHOT"));
    let job = persistence::create(&store, &actor, "Export", "最终权限复核").unwrap();
    let id = job["jobId"].as_str().unwrap();
    let output = file_output(&target, b"%PDF-denied").check_on_publish(
        crate::engine::reports::publication_check(
            &fixture.paths,
            vec![invoice_id],
            requests,
            false,
            "export-pdf",
        )
        .unwrap(),
    );
    store
        .transaction(|tx| change_user(tx, &actor, actor.id, "isActive", json!(false)))
        .unwrap();
    persistence::finish(&store, &actor, id, Ok(output)).unwrap();
    assert_eq!(
        persistence::get(&store, &actor, id).unwrap()["status"],
        "Failed"
    );
    assert_eq!(fs::read(&target).unwrap(), b"previous user file");
}

#[test]
fn conflicting_output_sources_fail_without_creating_a_directory_or_file() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let target = fixture.root.0.join("must-not-exist");
    for no_file in [false, true] {
        let job = persistence::create(&store, &fixture.actor, "Export", "无效输出来源").unwrap();
        let id = job["jobId"].as_str().unwrap();
        let mut result =
            TaskOutput::file("one.pdf".into(), "application/pdf", b"%PDF-file".to_vec());
        if no_file {
            result.file = None;
            result.destination = Some(target.clone());
        } else {
            result.directory =
                directory_output(&target, &[("two.pdf", b"%PDF-directory")]).directory;
        }
        persistence::finish(&store, &fixture.actor, id, Ok(result)).unwrap();
        assert_eq!(
            persistence::get(&store, &fixture.actor, id).unwrap()["status"],
            "Failed"
        );
        assert!(!target.exists());
    }
}

#[test]
fn publication_recovery_honors_the_injected_runtime_cache_root() {
    let mut fixture = Fixture::new();
    fixture.paths.cache_root = fixture.paths.data_root.join("Cache/Alternative");
    let store = fixture.store();
    let target = fixture.root.0.join("selected.pdf");
    fs::write(&target, b"old").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Export", "显式缓存根").unwrap();
    let id = job["jobId"].as_str().unwrap();
    drop(output::publish(&store, id, &file_output(&target, b"%PDF-new")).unwrap());
    assert!(
        fixture
            .paths
            .cache_root
            .join("TaskPublications")
            .join(id)
            .is_dir()
    );
    assert!(
        !fixture
            .paths
            .data_root
            .join("Cache/Native/TaskPublications")
            .join(id)
            .exists()
    );
    output::recover(&store).unwrap();
    assert_eq!(fs::read(target).unwrap(), b"old");
}

#[cfg(feature = "excel")]
#[test]
fn excel_exports_recheck_narrowed_data_scope_in_the_final_transaction() {
    let fixture = Fixture::new();
    let store = fixture.store();
    let admin = crate::engine::auth::current_actor(&store, 1).unwrap();
    let mut account = crate::engine::accounts::save(
        &store,
        &admin,
        0,
        json!({
            "username":"export-reader", "fullName":"导出验收", "role":"User", "isActive":true,
            "companyScope":"DEFAULT", "departmentId":"GENERAL", "resetPassword":"Export-Test-2026",
            "permissionGrants":[
                {"resourceKey":"document.jobs","action":"view","dataScope":"own"},
                {"resourceKey":"document.invoices","action":"view","dataScope":"company"},
                {"resourceKey":"document.query","action":"operate","dataScope":"company"}
            ]
        }),
    )
    .unwrap()["user"]
        .clone();
    let reader =
        crate::engine::auth::current_actor(&store, account["id"].as_i64().unwrap()).unwrap();
    let invoice_id = save_invoice(&store, &admin, "EXPORT-SCOPE");
    let checks = [
        crate::engine::excel::invoice_publication_check(invoice_id),
        crate::engine::invoice_query::publication_check(vec![invoice_id]),
    ];
    let jobs: Vec<_> = checks
        .into_iter()
        .map(|check| {
            let job = persistence::create(&store, &reader, "Export", "待发布 Excel").unwrap();
            (job["jobId"].as_str().unwrap().to_owned(), check)
        })
        .collect();
    account["expectedVersion"] = account["versionNumber"].clone();
    account["permissionGrants"] = json!([
        {"resourceKey":"document.jobs","action":"view","dataScope":"own"},
        {"resourceKey":"document.invoices","action":"view","dataScope":"own"},
        {"resourceKey":"document.query","action":"operate","dataScope":"own"}
    ]);
    crate::engine::accounts::save(&store, &admin, reader.id, account).unwrap();
    for (index, (id, check)) in jobs.into_iter().enumerate() {
        let target = fixture.root.0.join(format!("denied-{index}.xlsx"));
        persistence::finish(
            &store,
            &reader,
            &id,
            Ok(file_output(&target, b"xlsx").check_on_publish(check)),
        )
        .unwrap();
        let result = persistence::get(&store, &reader, &id).unwrap();
        assert_eq!(result["status"], "Failed");
        assert!(result["errorMessage"].as_str().unwrap().contains("权限"));
        assert!(!target.exists());
    }
}
