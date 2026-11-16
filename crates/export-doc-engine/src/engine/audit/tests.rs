use super::*;
#[test]
fn bounded_cleanup_crosses_batches_without_removing_business_records() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    let actor = auth::current_actor(&service.store, 1).unwrap();
    service
        .store
        .transaction(|tx| {
            for version in 1..=2105 {
                tx.append_audit_details(
                    &AuditWrite {
                        kind: "cleanup-canary",
                        record_id: actor.id,
                        version,
                        action: "edit",
                        actor_id: actor.id,
                        occurred_at: "2026-10-26T00:00:00Z",
                        note: "",
                    },
                    &json!({"newValues":{"summary":"x".repeat(2000)}}),
                )?;
            }
            Ok(())
        })
        .unwrap();
    for (limit, expected) in [(2001, 2001), (2001, 104), (2001, 0)] {
        let result: Value = serde_json::from_slice(
            &handle(
                &service,
                &actor,
                DELETE_AUDIT_LOGS_BY_CRITERIA,
                &[],
                &json!({"confirmed":true,"entityName":"cleanup-canary","maxCount":limit}),
            )
            .unwrap(),
        )
        .unwrap();
        assert_eq!(result["affectedCount"], expected);
    }
    assert_eq!(
        auth::current_actor(&service.store, actor.id).unwrap().id,
        actor.id
    );
    service
        .store
        .read(|tx| {
            let (_, rows) = tx.query_audits(
                &Criteria {
                    entity: "audit-maintenance".into(),
                    ..Default::default()
                }
                .query(0, 10, true, None),
            )?;
            assert_eq!(rows.len(), 2);
            assert_eq!(rows[0]["body"]["newValues"]["affectedCount"], 104);
            Ok(())
        })
        .unwrap();
}

#[test]
fn queued_audit_deletion_rechecks_the_current_grants_and_preserves_the_events() {
    let directory = crate::engine::test_support::ScratchDirectory::new();
    let service = directory.service();
    let actor = auth::current_actor(&service.store, 1).unwrap();
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
}
