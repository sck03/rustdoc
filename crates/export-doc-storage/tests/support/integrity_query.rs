use export_doc_storage::{AuditWrite, Connection};

pub fn exercise(connection: &Connection) {
    connection.begin().unwrap();
    for index in 0..=10_000 {
        connection
            .append_audit(&AuditWrite {
                kind: "history-integrity-contract",
                record_id: 91,
                version: index + 1,
                action: if index == 0 { "transition" } else { "edit" },
                actor_id: 1,
                occurred_at: "2026-10-25T00:00:00Z",
                note: "",
            })
            .unwrap();
    }
    let display = connection
        .history(Some("history-integrity-contract"), Some(91))
        .unwrap();
    assert_eq!(display.len(), 10_000);
    assert!(display.iter().all(|row| row["action"] == "edit"));
    assert!(
        connection
            .has_history_except("history-integrity-contract", 91, &["create", "edit"])
            .unwrap()
    );
    assert!(
        !connection
            .has_history_except("history-integrity-contract", 91, &["edit", "transition"])
            .unwrap()
    );
    assert!(
        !connection
            .has_history_except("history-integrity-contract", 92, &[])
            .unwrap()
    );
    assert!(
        !connection
            .has_history_except("other-kind", 91, &[])
            .unwrap()
    );
    connection.rollback().unwrap();
}
