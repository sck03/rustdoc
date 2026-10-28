use super::*;

fn file_output(path: &std::path::Path, bytes: &[u8]) -> TaskOutput {
    let mut output = TaskOutput::file("suggested.pdf".into(), "application/pdf", bytes.to_vec());
    output.destination = Some(path.into());
    output
}

#[test]
fn saved_location_keeps_the_selected_name_and_survives_download_copy_expiry() {
    let fixture = Fixture::new();
    let (store, jobs) = fixture.open();
    let target = fixture.root.join("custom-name.pdf");
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
    let target = fixture.root.join("existing.pdf");
    fs::write(&target, b"original user output").unwrap();
    let job = persistence::create(&store, &fixture.actor, "Export", "保存冲突").unwrap();
    let id = job["jobId"].as_str().unwrap();
    // Inject an optimistic SQL update failure after the filesystem write.
    let record_id = persistence::checked(&store.connection().unwrap(), &fixture.actor, id).unwrap()
        ["id"]
        .as_i64()
        .unwrap();
    store
        .transaction(|tx| {
            let mut record = persistence::checked(tx, &fixture.actor, id)?;
            record["versionNumber"] = json!(100000);
            tx.set_body(record["id"].as_i64().unwrap(), &record)?;
            Ok(())
        })
        .unwrap();
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
    let target = fixture.root.join("selected.pdf");
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
            crate::engine::store::save(
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
    let target = fixture.root.join("selected-folder");
    let job = persistence::create(&store, &fixture.actor, "Export", "文件夹输出").unwrap();
    let id = job["jobId"].as_str().unwrap();
    let make = || TaskOutput {
        file: None,
        managed_file: None,
        destination: None,
        publication: None,
        detail: "saved folder".into(),
        directory: Some(DirectoryOutput {
            path: target.clone(),
            files: vec![FileOutput {
                file_name: "one.pdf".into(),
                media_type: "application/pdf".into(),
                content: b"%PDF-1.7 test".to_vec(),
            }],
        }),
    };
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
    let target = fixture.root.join("denied.pdf");
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
            let mut user = crate::engine::store::get(tx, "users", fixture.actor.id)?;
            user["isActive"] = json!(false);
            crate::engine::store::save(
                tx,
                "users",
                fixture.actor.id,
                user,
                Some("admin".into()),
                &fixture.actor,
                "disable",
            )?;
            Ok(())
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
    let target = fixture.root.join("same.pdf");
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
    let folder = fixture.root.join("partial-folder");
    let output = TaskOutput {
        file: None,
        managed_file: None,
        destination: None,
        publication: None,
        detail: String::new(),
        directory: Some(DirectoryOutput {
            path: folder.clone(),
            files: vec![
                FileOutput {
                    file_name: "one.pdf".into(),
                    media_type: "application/pdf".into(),
                    content: b"%PDF-one".to_vec(),
                },
                FileOutput {
                    file_name: "two.pdf".into(),
                    media_type: "application/pdf".into(),
                    content: b"%PDF-two".to_vec(),
                },
            ],
        }),
    };
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
    let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_owned();
    fixture.paths = RuntimePaths::server(&workspace, &fixture.root.join("Data")).unwrap();
    let store = Store::open_postgres(&fixture.paths, &app).unwrap();
    let bootstrap = nonce().unwrap();
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
    let target = fixture.root.join("selected.pdf");
    fs::write(&target, b"previous user file").unwrap();
    let job = persistence::create(&store, &actor, "Export", "数据库失败恢复").unwrap();
    let id = job["jobId"].as_str().unwrap();
    store
        .transaction(|tx| {
            let mut record = persistence::checked(tx, &actor, id)?;
            record["versionNumber"] = json!(100000);
            tx.set_body(record["id"].as_i64().unwrap(), &record)?;
            Ok(())
        })
        .unwrap();
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

    let invoice = crate::invoice::InvoiceDraft::demo("2026-10-27", "PG-SNAPSHOT")
        .build()
        .unwrap();
    let saved = store
        .transaction(|tx| {
            crate::engine::store::save(
                tx,
                "invoices",
                0,
                serde_json::to_value(invoice)?,
                None,
                &actor,
                "create",
            )
        })
        .unwrap();
    let invoice_id = saved["id"].as_i64().unwrap();
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
            let mut record = crate::engine::store::get(tx, "invoices", invoice_id)?;
            record["invoiceNo"] = json!("PG-NEW");
            crate::engine::store::save(tx, "invoices", invoice_id, record, None, &actor, "edit")?;
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
        .transaction(|tx| {
            let mut account = crate::engine::store::get(tx, "users", actor.id)?;
            account["isActive"] = json!(false);
            crate::engine::store::save(
                tx,
                "users",
                actor.id,
                account,
                Some("admin".into()),
                &actor,
                "disable",
            )?;
            Ok(())
        })
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
    let target = fixture.root.join("must-not-exist");
    for no_file in [false, true] {
        let job = persistence::create(&store, &fixture.actor, "Export", "无效输出来源").unwrap();
        let id = job["jobId"].as_str().unwrap();
        let mut result =
            TaskOutput::file("one.pdf".into(), "application/pdf", b"%PDF-file".to_vec());
        if no_file {
            result.file = None;
            result.destination = Some(target.clone());
        } else {
            result.directory = Some(DirectoryOutput {
                path: target.clone(),
                files: vec![FileOutput {
                    file_name: "two.pdf".into(),
                    media_type: "application/pdf".into(),
                    content: b"%PDF-directory".to_vec(),
                }],
            });
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
    let target = fixture.root.join("selected.pdf");
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
    let invoice = crate::invoice::InvoiceDraft::demo("2026-10-28", "EXPORT-SCOPE")
        .build()
        .unwrap();
    let saved = store
        .transaction(|tx| {
            crate::engine::store::save(
                tx,
                "invoices",
                0,
                serde_json::to_value(invoice)?,
                None,
                &admin,
                "create",
            )
        })
        .unwrap();
    let invoice_id = saved["id"].as_i64().unwrap();
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
        let target = fixture.root.join(format!("denied-{index}.xlsx"));
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
