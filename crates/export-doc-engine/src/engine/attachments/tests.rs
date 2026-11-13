use super::*;
use crate::engine::test_support::{ScratchDirectory, workspace_root};
use crate::paths::RuntimePaths;
use export_doc_domain::permissions::ProductEdition;
use std::fs;
struct Fixture {
    store: Store,
    actor: Actor,
    id: i64,
    root: ScratchDirectory,
}
impl Fixture {
    fn invoke_json(&self, operation: Operation, body: Value) -> Result<Value> {
        let bytes = handle(
            &self.store,
            &self.actor,
            operation,
            &[("id", self.id.to_string())],
            &[],
            body,
        )?;
        Ok(serde_json::from_slice(&bytes)?)
    }
}
fn fixture() -> Fixture {
    let root = ScratchDirectory::new();
    let mut store =
        Store::open(&RuntimePaths::server(&workspace_root(), &root.0).unwrap()).unwrap();
    store.edition = ProductEdition::Full;
    auth::seed(&store).unwrap();
    let actor =
        auth::current_actor_in(&store.connection().unwrap(), 1, ProductEdition::Full).unwrap();
    let (invoice, category) = store
        .transaction_as(&actor, |tx, current| {
            let invoice = store::save(
                tx,
                "invoices",
                0,
                json!({"invoiceNo":"ATT-READ-TEST","type":"Customs","status":"Draft","items":[]}),
                None,
                current,
                "create",
            )?;
            let category = store::save(
                tx,
                "attachment-categories",
                0,
                json!({"name":"读取测试","isActive":true}),
                None,
                current,
                "create",
            )?;
            Ok((
                invoice["id"].as_i64().unwrap(),
                category["id"].as_i64().unwrap(),
            ))
        })
        .unwrap();
    let row = upload(
        &store,
        &actor,
        invoice,
        json!({"title":"读取测试","uploadKey":"fixture","categoryId":category}),
        "fixture.bin",
        &mut std::io::Cursor::new(b"fixture"),
        7,
    )
    .unwrap();
    Fixture {
        store,
        actor,
        id: row["id"].as_i64().unwrap(),
        root,
    }
}

#[test]
fn attachment_reads_reject_a_stale_actor_after_account_deactivation() {
    let f = fixture();
    let (_, bytes) = read(&f.store, &f.actor, DOWNLOAD_BUSINESS_ATTACHMENT, f.id, 1).unwrap();
    assert_eq!(bytes, b"fixture");
    f.store
        .transaction_as(&f.actor, |tx, current| {
            let mut user = store::get(tx, "users", current.id)?;
            user["isActive"] = json!(false);
            store::save(tx, "users", current.id, user, None, current, "deactivate")?;
            Ok(())
        })
        .unwrap();
    for operation in [LIST_BUSINESS_ATTACHMENTS, GET_BUSINESS_ATTACHMENT] {
        assert_eq!(
            f.invoke_json(operation, Value::Null).unwrap_err().status,
            Some(403)
        );
    }
    assert_eq!(
        read(&f.store, &f.actor, DOWNLOAD_BUSINESS_ATTACHMENT, f.id, 1)
            .unwrap_err()
            .status,
        Some(403)
    );
}

#[test]
fn oversized_stored_attachment_is_rejected_and_extension_case_is_preserved() {
    let f = fixture();
    let destination = f.root.0.join("copy.BIN");
    handle(
        &f.store,
        &f.actor,
        SAVE_BUSINESS_ATTACHMENT_TO_PATH,
        &[("id", f.id.to_string()), ("revision", "1".into())],
        &[],
        json!({"destinationPath":destination}),
    )
    .unwrap();
    assert_eq!(fs::read(&destination).unwrap(), b"fixture");
    let mut prepared = f.store.files.prepare(b"oversized metadata").unwrap();
    prepared.object.size = FILE_LIMIT as u64 + 1;
    f.store
        .transaction(|tx| {
            tx.publish_file(
                &prepared,
                "attachment:2",
                f.id,
                "oversized.bin",
                "application/octet-stream",
            )?;
            Ok(())
        })
        .unwrap();
    assert_eq!(
        read(&f.store, &f.actor, DOWNLOAD_BUSINESS_ATTACHMENT, f.id, 2)
            .unwrap_err()
            .status,
        Some(503)
    );
}

#[test]
fn a_new_attachment_has_an_empty_event_history_and_can_be_archived() {
    let f = fixture();
    let initial = f.invoke_json(GET_BUSINESS_ATTACHMENT, Value::Null).unwrap();
    assert_eq!(initial["eventCount"], 0);
    assert_eq!(initial["events"], json!([]));
    assert_eq!(initial["revisions"].as_array().unwrap().len(), 1);
    f.invoke_json(
        UPDATE_BUSINESS_ATTACHMENT,
        json!({"versionNumber":initial["attachment"]["versionNumber"],"currentRevision":1,"isArchived":true,"note":"归档测试"}),
    )
    .unwrap();
    let archived = f.invoke_json(GET_BUSINESS_ATTACHMENT, Value::Null).unwrap();
    assert_eq!(archived["eventCount"], 1);
    assert_eq!(archived["events"][0]["action"], "Archive");
    assert_eq!(archived["attachment"]["isArchived"], true);
}

#[test]
fn corrupt_attachment_events_are_not_reported_as_an_empty_history() {
    let f = fixture();
    f.store
        .transaction(|tx| {
            let mut row = store::get(tx, "attachments", f.id)?;
            row["events"] = json!("corrupted");
            store::save(tx, "attachments", f.id, row, None, &f.actor, "corrupt-test")?;
            Ok(())
        })
        .unwrap();
    assert_eq!(
        f.invoke_json(GET_BUSINESS_ATTACHMENT, Value::Null)
            .unwrap_err()
            .status,
        Some(503)
    );
    let before = store::get(&f.store.connection().unwrap(), "attachments", f.id).unwrap();
    assert_eq!(
        f.invoke_json(
            UPDATE_BUSINESS_ATTACHMENT,
            json!({"versionNumber":before["versionNumber"],"currentRevision":1,"isArchived":true,"note":"不得覆盖损坏历史"}),
        )
        .unwrap_err()
        .status,
        Some(503)
    );
    let saved = store::get(&f.store.connection().unwrap(), "attachments", f.id).unwrap();
    assert_eq!(saved["events"], "corrupted");
    assert_eq!(saved["isArchived"], false);
    assert_eq!(saved["versionNumber"], before["versionNumber"]);
}

#[test]
fn historical_revisions_pass_fifty_without_growing_the_business_record() {
    let f = fixture();
    let mut current = store::get(&f.store.connection().unwrap(), "attachments", f.id).unwrap();
    for revision in 2..=65 {
        let bytes = format!("version {revision}");
        current = upload(&f.store,&f.actor,current["invoiceId"].as_i64().unwrap(),
            json!({"attachmentId":f.id,"expectedVersion":current["versionNumber"],"title":current["title"],"categoryId":current["categoryId"],"uploadKey":format!("revision-{revision}")}),
            "fixture.bin", &mut std::io::Cursor::new(bytes.as_bytes()), bytes.len() as u64).unwrap();
    }
    let metadata = store::get(&f.store.connection().unwrap(), "attachments", f.id).unwrap();
    assert!(metadata.get("revisions").is_none());
    assert_eq!(metadata["latestRevision"], 65);
    let first = f.invoke_json(GET_BUSINESS_ATTACHMENT, Value::Null).unwrap();
    assert_eq!(first["revisionCount"], 65);
    assert_eq!(first["revisions"].as_array().unwrap().len(), 30);
    assert_eq!(first["revisions"][0]["revision"], 65);
    let second: Value = serde_json::from_slice(
        &handle(
            &f.store,
            &f.actor,
            GET_BUSINESS_ATTACHMENT,
            &[("id", f.id.to_string())],
            &[("revisionBefore", first["revisionNextCursor"].to_string())],
            Value::Null,
        )
        .unwrap(),
    )
    .unwrap();
    assert_eq!(second["revisions"][0]["revision"], 35);
    assert_eq!(
        read(&f.store, &f.actor, DOWNLOAD_BUSINESS_ATTACHMENT, f.id, 1)
            .unwrap()
            .1,
        b"fixture"
    );
}
