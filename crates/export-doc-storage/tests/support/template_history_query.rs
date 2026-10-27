use export_doc_storage::{Connection, RecordWrite, TemplateVersionQuery};
use serde_json::json;

fn insert(tx: &Connection, kind: &str, template: i64, number: i64, content: &str) {
    let mut body = json!({"templateKind":kind,"templateId":template,"versionNumber":1,"ownerUserId":7,
        "content":{"versionNumber":number,"name":"快照","contentHtml":content}});
    let id = tx
        .insert(&RecordWrite {
            kind: "template-versions",
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    tx.set_body(id, &body).unwrap();
}
pub fn exercise(tx: &Connection) {
    tx.begin().unwrap();
    let content = "多字节正文".repeat(4096);
    for number in 1..=25 {
        insert(tx, "report-templates", 202, number, &content);
    }
    insert(tx, "email-templates", 202, 99, &content);
    insert(tx, "report-templates", 203, 99, &content);
    let q = TemplateVersionQuery {
        kind: "report-templates",
        template_id: 202,
        offset: 20,
        limit: 2,
    };
    let usage = tx.template_history_usage(&q, 20).unwrap();
    assert_eq!(usage.count, 25);
    assert_eq!(usage.eligible_count, 5);
    assert!(usage.bytes >= content.len() as u64 * 25);
    assert!(usage.eligible_bytes >= content.len() as u64 * 5);
    let candidates = tx.template_history_candidates(&q).unwrap();
    assert_eq!(
        candidates
            .iter()
            .map(|row| row["versionNumber"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        [5, 4]
    );
    assert!(candidates.iter().all(|row| row["recordVersion"] == 1
        && row["byteLength"].as_u64().unwrap() >= content.len() as u64));
    assert!(serde_json::to_vec(&candidates).unwrap().len() < 256);
    let id = candidates[0]["id"].as_i64().unwrap();
    assert!(tx.delete("template-versions", id, 1).unwrap());
    assert_eq!(tx.template_history_usage(&q, 20).unwrap().eligible_count, 4);
    let absent = TemplateVersionQuery {
        template_id: 999,
        ..q
    };
    let empty = tx.template_history_usage(&absent, 20).unwrap();
    assert_eq!(empty.count, 0);
    assert_eq!(empty.bytes, 0);
    assert!(tx.template_history_candidates(&absent).unwrap().is_empty());
    tx.rollback().unwrap();
}
