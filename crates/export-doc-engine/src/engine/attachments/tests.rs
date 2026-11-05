use super::*;
use crate::paths::RuntimePaths;
use export_doc_domain::permissions::ProductEdition;
use std::{fs, path::PathBuf};

struct OwnedRoot {
    path: PathBuf,
    parent: PathBuf,
}
impl Drop for OwnedRoot {
    fn drop(&mut self) {
        let result = (|| -> std::result::Result<(), String> {
            paths::ensure_safe_absolute(&self.path)?;
            let resolved = self
                .path
                .canonicalize()
                .map_err(|error| error.to_string())?;
            if resolved.parent() != Some(self.parent.as_path()) || resolved.join(".git").exists() {
                return Err("测试目录边界已变化，拒绝清理。".into());
            }
            fs::remove_dir_all(resolved).map_err(|error| error.to_string())
        })();
        if let Err(error) = result {
            if std::thread::panicking() {
                eprintln!("附件测试清理失败：{error}");
            } else {
                panic!("附件测试清理失败：{error}");
            }
        }
    }
}
struct Fixture {
    store: Store,
    actor: Actor,
    id: i64,
    root: OwnedRoot,
}
fn fixture() -> Fixture {
    let repo = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .canonicalize()
        .unwrap();
    let parent = repo.join(".codex-runtime/attachment-read-tests");
    paths::ensure_safe_absolute(&parent).unwrap();
    fs::create_dir_all(&parent).unwrap();
    let parent = parent.canonicalize().unwrap();
    let root = OwnedRoot {
        path: parent.join(paths::nonce().unwrap()),
        parent,
    };
    fs::create_dir(&root.path).unwrap();
    let mut store = Store::open(&RuntimePaths::server(&repo, &root.path).unwrap()).unwrap();
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
        b"fixture",
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
    let parameters = [("id", f.id.to_string())];
    for operation in [LIST_BUSINESS_ATTACHMENTS, GET_BUSINESS_ATTACHMENT] {
        assert_eq!(
            handle(&f.store, &f.actor, operation, &parameters, &[], Value::Null)
                .unwrap_err()
                .status,
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
    let destination = f.root.path.join("copy.BIN");
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
    let bytes = vec![1; FILE_LIMIT + 1];
    f.store
        .transaction(|tx| {
            tx.insert_blob(&BlobWrite {
                kind: "attachment:2",
                record_id: f.id,
                file_name: "oversized.bin",
                media_type: "application/octet-stream",
                digest: &digest(&bytes),
                content: &bytes,
                created_at: &store::timestamp(),
            })?;
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
    let parameters = [("id", f.id.to_string())];
    let details = || -> Value {
        serde_json::from_slice(
            &handle(
                &f.store,
                &f.actor,
                GET_BUSINESS_ATTACHMENT,
                &parameters,
                &[],
                Value::Null,
            )
            .unwrap(),
        )
        .unwrap()
    };
    let initial = details();
    assert_eq!(initial["eventCount"], 0);
    assert_eq!(initial["events"], json!([]));
    assert_eq!(initial["revisions"].as_array().unwrap().len(), 1);
    handle(
        &f.store,
        &f.actor,
        UPDATE_BUSINESS_ATTACHMENT,
        &parameters,
        &[],
        json!({"versionNumber":initial["attachment"]["versionNumber"],"currentRevision":1,"isArchived":true,"note":"归档测试"}),
    )
    .unwrap();
    let archived = details();
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
        handle(
            &f.store,
            &f.actor,
            GET_BUSINESS_ATTACHMENT,
            &[("id", f.id.to_string())],
            &[],
            Value::Null
        )
        .unwrap_err()
        .status,
        Some(503)
    );
    let before = store::get(&f.store.connection().unwrap(), "attachments", f.id).unwrap();
    assert_eq!(
        handle(
            &f.store,
            &f.actor,
            UPDATE_BUSINESS_ATTACHMENT,
            &[("id", f.id.to_string())],
            &[],
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
