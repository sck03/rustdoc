use export_doc_storage::{Connection, RecordWrite};
use serde_json::json;
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let source = json!({"version":3,"resources":[{"id":"img-sample.png"}],"largeText":"private-body".repeat(4096)}).to_string();
    let mut template_record_id = None;
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
        if kind == "template-versions" {
            body["templateId"] = json!(template_record_id.unwrap());
        }
        body["versionNumber"] = json!(1);
        body["ownerUserId"] = json!(1);
        body["departmentId"] = json!("A");
        let record_id = c.insert(&RecordWrite::unkeyed(kind, &body)).unwrap();
        if kind == "report-templates" {
            template_record_id = Some(record_id);
        }
        let (_, rows) = c.resource_references(kind, None, None, 200).unwrap();
        let row = rows.iter().find(|row| row["id"] == body["id"]).unwrap();
        assert!(!row.to_string().contains("private-body"));
        if kind != "invoices" {
            assert_eq!(row["validDocument"], true);
            assert_eq!(row["resources"][0]["id"], "img-sample.png");
        }
        let (_, targeted) = c
            .resource_references(kind, Some(&["img-sample.png"]), None, 200)
            .unwrap();
        assert_eq!(targeted.len(), 1);
        assert!(
            c.resource_references(kind, Some(&["missing.png"]), None, 200)
                .unwrap()
                .1
                .is_empty()
        );
        assert!(
            c.resource_references(kind, Some(&[]), None, 200)
                .unwrap()
                .1
                .is_empty()
        );
    }
    let mut malformed = json!({"versionNumber":1,"ownerUserId":1,"departmentId":"A","companyScope":"REF-Q","contentHtml":json!({"version":"3","resources":[]}).to_string()});
    let id = c
        .insert(&RecordWrite::unkeyed("report-templates", &malformed))
        .unwrap();
    malformed["id"] = json!(id);
    c.set_body(id, &malformed).unwrap();
    let (_, rows) = c
        .resource_references("report-templates", None, None, 200)
        .unwrap();
    assert_eq!(
        rows.iter().find(|row| row["id"] == id).unwrap()["validDocument"],
        false
    );
    let mut expected = Vec::new();
    for i in 0..451 {
        let id = c.insert(&RecordWrite::unkeyed("exporters", &json!({"id":-1,"versionNumber":1,"companyScope":"REF-Q","departmentId":"A","ownerUserId":1,"docSealPath":format!("Files/Seals/img-{i}.png")}))).unwrap();
        expected.push(id);
    }
    let started = std::time::Instant::now();
    let mut cursor = None;
    let mut count = 0;
    loop {
        let (next, rows) = c
            .resource_references("exporters", None, cursor, 200)
            .unwrap();
        if rows.is_empty() {
            break;
        }
        assert!(rows.len() <= 200);
        assert_eq!(next, Some(expected[expected.len() - count - rows.len()]));
        assert!(
            rows.iter().all(|row| row["id"] == -1),
            "DTO ids do not control the physical cursor"
        );
        cursor = next;
        count += rows.len();
    }
    assert_eq!(count, 451);
    let (_, targeted) = c
        .resource_references("exporters", Some(&["img-0.png", "img-450.png"]), None, 200)
        .unwrap();
    assert_eq!(
        targeted.len(),
        2,
        "unrelated exporter references stay inside storage"
    );
    println!(
        "{} resource keyset: {count} references in {:?}",
        c.provider(),
        started.elapsed()
    );
    c.rollback().unwrap();
}
