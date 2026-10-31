//! Relational organization fixtures shared by both adapter contracts.
use export_doc_storage::{Connection, RecordWrite};
use serde_json::json;

pub fn unit(tx: &Connection, code: &str, departments: &[&str]) {
    let mut company = json!({"code":code,"name":code,"unitKind":"Company","isActive":true,"versionNumber":1,"ownerUserId":0,"companyScope":code});
    let id = tx
        .insert(&RecordWrite {
            kind: "companies",
            identity: Some(&code.to_lowercase()),
            body: &company,
        })
        .unwrap();
    company["id"] = json!(id);
    tx.set_body(id, &company).unwrap();
    for department in departments {
        let mut body = json!({"companyCode":code,"companyScope":code,"code":department,"name":department,"isActive":true,"versionNumber":1,"ownerUserId":0});
        let id = tx
            .insert(&RecordWrite {
                kind: "departments",
                identity: Some(&format!("{code}:{department}")),
                body: &body,
            })
            .unwrap();
        body["id"] = json!(id);
        tx.set_body(id, &body).unwrap();
    }
}
