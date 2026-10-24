use export_doc_storage::{Connection, RecordWrite};
use serde_json::json;
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let source = json!({"version":3,"resources":[{"id":"img-sample.png"}],"largeText":"private-body".repeat(4096)}).to_string();
    for (kind, mut body) in [
        (
            "invoices",
            json!({"id":1,"companyScope":"REF-Q","departmentId":"A","ownerUserId":1,"shippingMarksType":"Image","shippingMarksImage":"Files/ShippingMarks/img-sample.png","items":[{"private":"private-body".repeat(4096)}]}),
        ),
        (
            "report-templates",
            json!({"id":2,"companyScope":"REF-Q","contentHtml":source}),
        ),
        (
            "template-versions",
            json!({"id":3,"companyScope":"REF-Q","templateKind":"report-templates","templateId":2,"content":{"contentHtml":source}}),
        ),
    ] {
        body["versionNumber"] = json!(1);
        body["ownerUserId"] = json!(1);
        body["departmentId"] = json!("A");
        c.insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
        let (_, rows) = c.resource_references(kind, 0, 200).unwrap();
        let row = rows.iter().find(|row| row["id"] == body["id"]).unwrap();
        assert!(!row.to_string().contains("private-body"));
        if kind != "invoices" {
            assert_eq!(row["validDocument"], true);
            assert_eq!(row["resources"][0]["id"], "img-sample.png");
        }
    }
    let mut malformed = json!({"versionNumber":1,"ownerUserId":1,"departmentId":"A","companyScope":"REF-Q","contentHtml":json!({"version":"3","resources":[]}).to_string()});
    let id = c
        .insert(&RecordWrite {
            kind: "report-templates",
            identity: None,
            body: &malformed,
        })
        .unwrap();
    malformed["id"] = json!(id);
    c.set_body(id, &malformed).unwrap();
    let (_, rows) = c.resource_references("report-templates", 0, 200).unwrap();
    assert_eq!(
        rows.iter().find(|row| row["id"] == id).unwrap()["validDocument"],
        false
    );
    c.rollback().unwrap();
}
