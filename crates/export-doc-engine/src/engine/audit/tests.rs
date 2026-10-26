use super::*;
use crate::paths::{RuntimePaths, nonce};
#[test]
fn queued_audit_deletion_rechecks_the_current_grants_and_preserves_the_events() {
    let workspace = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_owned();
    let root = workspace
        .join(".codex-runtime/authorization-tests")
        .join(nonce().unwrap());
    std::fs::create_dir_all(&root).unwrap();
    let _cleanup = crate::test_support::ScratchDirectory(root.clone());
    let service =
        NativeService::open(RuntimePaths::server(&root, &root.join("Data")).unwrap()).unwrap();
    let actor = auth::current_actor_in(
        &*service.store.connection().unwrap(),
        1,
        service.store.edition,
    )
    .unwrap();
    service
        .store
        .transaction(|tx| {
            tx.append_audit(&AuditWrite {
                kind: "authorization-canary",
                record_id: 99,
                version: 1,
                action: "create",
                actor_id: actor.id,
                occurred_at: "2026-10-26T00:00:00Z",
                note: "",
            })?;
            Ok(())
        })
        .unwrap();
    std::thread::scope(|scope| {
        let (entered, waiting) = std::sync::mpsc::channel();
        let (release, released) = std::sync::mpsc::channel();
        let store = &service.store;
        let current = &actor;
        let writer = scope.spawn(move || {
            store.transaction(|tx| {
                entered.send(()).unwrap();
                released.recv().unwrap();
                let mut user = super::super::store::get(tx, "users", current.id)?;
                user["role"] = json!("Employee");
                user["permissionMode"] = json!("Independent");
                user["permissionGrants"] = json!([]);
                super::super::store::save(tx, "users", current.id, user, None, current, "edit")?;
                Ok(())
            })
        });
        waiting.recv().unwrap();
        let request = scope.spawn(|| {
            handle(
                &service,
                &actor,
                DELETE_AUDIT_LOGS_BY_CRITERIA,
                &[],
                &json!({"confirmed":true,"entityName":"authorization-canary"}),
            )
        });
        release.send(()).unwrap();
        writer.join().unwrap().unwrap();
        assert_eq!(request.join().unwrap().unwrap_err().status, Some(403));
    });
    assert!(
        service
            .store
            .connection()
            .unwrap()
            .audit_events(i64::MAX, 200)
            .unwrap()
            .iter()
            .any(|row| row["kind"] == "authorization-canary")
    );
    service.close().unwrap();
}
