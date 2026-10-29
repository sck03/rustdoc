//! Relational organization fixtures shared by both adapter contracts.
use export_doc_storage::{Connection, RecordWrite};
use serde_json::json;

pub fn unit(tx: &Connection, code: &str, departments: &[&str]) {
    let company = json!({"code":code,"name":code,"unitKind":"Company","isActive":true,"versionNumber":1,"ownerUserId":0,"companyScope":code});
    tx.insert(&RecordWrite {
        kind: "companies",
        identity: Some(&code.to_lowercase()),
        body: &company,
    })
    .unwrap();
    for department in departments {
        let body = json!({"companyCode":code,"companyScope":code,"code":department,"name":department,"isActive":true,"versionNumber":1,"ownerUserId":0});
        tx.insert(&RecordWrite {
            kind: "departments",
            identity: Some(&format!("{code}:{department}")),
            body: &body,
        })
        .unwrap();
    }
}
