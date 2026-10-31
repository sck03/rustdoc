use export_doc_storage::{BlobWrite, Connection, ErrorKind, RecordWrite};
#[path = "support/business_query.rs"]
mod business_query;
#[path = "support/capacity_query.rs"]
mod capacity_query;
#[path = "support/communication_query.rs"]
mod communication_query;
#[path = "support/crm_query.rs"]
mod crm_query;
#[path = "support/foundation_query.rs"]
mod foundation_query;
#[path = "support/import_preview_query.rs"]
mod import_preview_query;
#[path = "support/integrity_query.rs"]
mod integrity_query;
#[path = "support/maintenance_query.rs"]
mod maintenance_query;
#[path = "support/oa_period_query.rs"]
mod oa_period_query;
#[path = "support/office_query.rs"]
mod office_query;
#[path = "support/organization_fixture.rs"]
mod organization_fixture;
#[path = "support/organization_query.rs"]
mod organization_query;
#[path = "support/personnel_query.rs"]
mod personnel_query;
#[cfg(feature = "postgres")]
#[path = "support/pool_recovery.rs"]
mod pool_recovery;
#[cfg(feature = "postgres")]
#[path = "support/query_plans.rs"]
mod query_plans;
#[path = "support/related_query.rs"]
mod related_query;
#[path = "support/resource_query.rs"]
mod resource_query;
#[path = "support/scaling_query.rs"]
mod scaling_query;
#[path = "support/supplier_overview_query.rs"]
mod supplier_overview_query;
#[path = "support/template_history_query.rs"]
mod template_history_query;
#[path = "support/template_query.rs"]
mod template_query;
#[path = "support/workflow_fixture.rs"]
mod workflow_fixture;
use serde_json::json;
use std::{
    fs,
    path::PathBuf,
    time::{SystemTime, UNIX_EPOCH},
};

fn scratch() -> PathBuf {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join(".codex-runtime")
        .join("storage-tests")
        .join(format!(
            "{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
    fs::create_dir_all(&root).unwrap();
    root
}

#[test]
fn sqlite_rejects_earlier_or_duplicate_schema_markers() {
    let root = scratch();
    for (name, versions) in [
        ("earlier", vec![1_i64]),
        ("previous", vec![export_doc_storage::SCHEMA_VERSION - 1]),
        ("duplicate", vec![export_doc_storage::SCHEMA_VERSION; 2]),
    ] {
        let file = root.join(format!("{name}.db"));
        let raw = rusqlite::Connection::open(&file).unwrap();
        raw.execute("CREATE TABLE schema_version(version INTEGER NOT NULL)", [])
            .unwrap();
        for version in versions {
            raw.execute("INSERT INTO schema_version(version) VALUES(?1)", [version])
                .unwrap();
        }
        drop(raw);
        let result = Connection::sqlite(&file);
        assert!(matches!(result,Err(ref cause) if cause.kind==ErrorKind::Unavailable));
    }
    fs::remove_dir_all(root).unwrap();
}
#[test]
fn sqlite_model_and_structure_validation_preserve_rejected_files() {
    let root = scratch();
    for (name, statement) in [
        ("model", "UPDATE schema_model SET model='unknown-baseline'"),
        ("structure", "DROP INDEX positions_name"),
    ] {
        let file = root.join(format!("{name}.db"));
        drop(Connection::sqlite(&file).unwrap());
        let raw = rusqlite::Connection::open(&file).unwrap();
        raw.execute(statement, []).unwrap();
        drop(raw);
        let before = fs::read(&file).unwrap();
        assert!(Connection::sqlite(&file).is_err());
        assert_eq!(
            fs::read(&file).unwrap(),
            before,
            "rejection must not rewrite a database"
        );
    }
    fs::remove_dir_all(root).unwrap();
}

fn roundtrip(connection: &Connection) {
    foundation_query::exercise(connection);
    maintenance_query::exercise(connection);
    organization_query::exercise(connection);
    template_history_query::exercise(connection);
    integrity_query::exercise(connection);
    related_query::exercise(connection);
    supplier_overview_query::exercise(connection);
    import_preview_query::exercise(connection);
    capacity_query::exercise(connection);
    office_query::exercise(connection);
    oa_period_query::exercise(connection);
    resource_query::exercise(connection);
    business_query::exercise(connection);
    personnel_query::exercise(connection);
    scaling_query::exercise(connection);
    template_query::exercise(connection);
    communication_query::exercise(connection);
    crm_query::exercise(connection);
    connection.begin().unwrap();
    connection
        .append_audit_details(
            &export_doc_storage::AuditWrite {
                kind: "invoices",
                record_id: 123,
                version: 1,
                action: "create",
                actor_id: 7,
                occurred_at: "2026-09-17T00:00:00Z",
                note: "Business history",
            },
            &json!({"newValues":{"status":"Draft"}}),
        )
        .unwrap();
    connection.commit().unwrap();
    let audit = connection.audit_events(i64::MAX, 20).unwrap();
    assert_eq!(audit[0]["body"]["newValues"]["status"], "Draft");
    let audit_id = audit[0]["id"].as_i64().unwrap();
    connection.begin().unwrap();
    assert_eq!(connection.delete_audits(&[audit_id]).unwrap(), 1);
    connection.rollback().unwrap();
    assert_eq!(connection.audit_events(i64::MAX, 20).unwrap().len(), 1);
    connection.begin().unwrap();
    assert_eq!(connection.delete_audits(&[audit_id]).unwrap(), 1);
    connection.commit().unwrap();
    assert!(connection.audit_events(i64::MAX, 20).unwrap().is_empty());
    assert_eq!(
        connection
            .history(Some("invoices"), Some(123))
            .unwrap()
            .len(),
        1
    );
    let body = json!({"versionNumber":1,"ownerUserId":7,"companyScope":"公司é","departmentId":"销售部","status":"Pending","submissionNumber":1,"amount":123456.789,"items":["零件",null]});
    connection.begin().unwrap();
    let id = connection
        .insert(&RecordWrite {
            kind: "contract",
            identity: Some("唯一记录"),
            body: &body,
        })
        .unwrap();
    connection.set_body(id, &body).unwrap();
    connection
        .set_credential(id, &[1; 32], &[2; 32], 600000)
        .unwrap();
    connection
        .insert_blob(&BlobWrite {
            kind: "document",
            record_id: id,
            file_name: "资料.pdf",
            media_type: "application/pdf",
            digest: "test",
            content: b"%PDF-test",
            created_at: "2026-09-16T00:00:00Z",
        })
        .unwrap();
    connection.commit().unwrap();
    assert_eq!(connection.get("contract", id).unwrap(), Some(body.clone()));
    assert_eq!(
        connection.credential(id).unwrap().unwrap().iterations,
        600000
    );
    assert_eq!(
        connection.blob(id, "document").unwrap().unwrap().content,
        b"%PDF-test"
    );

    connection.begin().unwrap();
    let duplicate = connection
        .insert(&RecordWrite {
            kind: "contract",
            identity: Some("唯一记录"),
            body: &body,
        })
        .unwrap_err();
    assert_eq!(duplicate.kind, ErrorKind::Conflict);
    connection.rollback().unwrap();
    assert_eq!(connection.all("contract").unwrap().len(), 1);

    let mut edited = body.clone();
    edited["versionNumber"] = json!(2);
    connection.begin().unwrap();
    assert!(
        !connection
            .update(
                id,
                99,
                &RecordWrite {
                    kind: "contract",
                    identity: Some("唯一记录"),
                    body: &edited
                }
            )
            .unwrap()
    );
    assert!(
        connection
            .update(
                id,
                1,
                &RecordWrite {
                    kind: "contract",
                    identity: Some("唯一记录"),
                    body: &edited
                }
            )
            .unwrap()
    );
    connection.rollback().unwrap();
    assert_eq!(connection.get("contract", id).unwrap(), Some(body));
    connection.begin().unwrap();
    connection.delete_blobs(id).unwrap();
    assert!(connection.delete("contract", id, 1).unwrap());
    connection.commit().unwrap();
    assert!(connection.credential(id).unwrap().is_none());
    workflow_fixture::exercise(connection);
}

#[test]
fn sqlite_transactions_blobs_and_backup_use_one_database() {
    let root = scratch();
    {
        let connection = Connection::sqlite(&root.join("data.db")).unwrap();
        roundtrip(&connection);
        connection
            .set_settings("settings", 1, &json!({"revision":1,"name":"保存前"}))
            .unwrap();
        connection.backup(&root.join("backup.db")).unwrap();
        connection
            .set_settings("settings", 2, &json!({"revision":2,"name":"保存后"}))
            .unwrap();
        connection.restore(&root.join("backup.db")).unwrap();
        assert_eq!(
            connection.settings("settings").unwrap().unwrap()["name"],
            "保存前"
        );
        let missing = root.join("missing.db");
        assert!(connection.verify_backup(&missing).is_err());
        assert!(!missing.exists());
    }
    fs::remove_dir_all(root).unwrap();
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires an isolated PostgreSQL 18 cluster and separate maintenance/app roles"]
fn postgres_matches_the_sqlite_contract_and_holds_the_instance_lock() {
    let maintenance = std::env::var("EXPORTDOC_TEST_POSTGRES_MAINTENANCE")
        .expect("isolated maintenance connection");
    let app = std::env::var("EXPORTDOC_TEST_POSTGRES_APP").expect("isolated app connection");
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    {
        let mut raw = postgres::Client::connect(&maintenance, postgres::NoTls).unwrap();
        raw.batch_execute(r#"SET ROLE native_owner; INSERT INTO settings VALUES ('v5-preserved',1,'{"value":"原有业务数据"}'::jsonb);"#).unwrap();
    }
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    assert!(
        Connection::postgres(&maintenance).is_err(),
        "maintenance credentials must not serve ordinary requests"
    );
    {
        let connection = Connection::postgres(&app).unwrap();
        assert_eq!(
            connection.settings("v5-preserved").unwrap().unwrap()["value"],
            "原有业务数据"
        );
        assert!(matches!(Connection::postgres(&app), Err(error) if error.kind == ErrorKind::Busy));
        roundtrip(&connection);
        assert_eq!(connection.provider(), "PostgreSQL");
    }
    Connection::postgres(&app).unwrap().health().unwrap();
    pool_recovery::exercise(&app, &maintenance);
    query_plans::exercise(&maintenance);
}
