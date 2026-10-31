use export_doc_storage::{Connection, DataScope, ErrorKind, RecordQuery, RecordWrite};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, mut body: Value, owner: i64, company: &str) -> i64 {
    body["companyScope"] = json!(company);
    body["departmentId"] = json!("D");
    body["ownerUserId"] = json!(owner);
    body["versionNumber"] = json!(1);
    let id = c
        .insert(&RecordWrite {
            kind,
            identity: None,
            body: &body,
        })
        .unwrap();
    body["id"] = json!(id);
    c.set_body(id, &body).unwrap();
    id
}
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let mut suppliers = vec![];
    for (name, owner, company) in [
        ("甲", 71, "SUPPLIER-Q"),
        ("乙", 72, "SUPPLIER-Q"),
        ("未评", 71, "SUPPLIER-Q"),
        ("另一单位", 71, "OTHER-SUPPLIER-Q"),
    ] {
        suppliers.push(insert(c, "suppliers", json!({"name":name,"status":"Active","category":"制造","unusedLargeBody":"not projected"}), owner, company));
    }
    for (supplier, date, quality, state, conclusion, notes) in [
        (0, "2026-10-20", 1, "Confirmed", "合格", json!("旧评价")),
        (0, "2026-10-29", 3, "Confirmed", "观察", json!("同日旧编号")),
        (0, "2026-10-29", 5, "Confirmed", "优先合作", Value::Null),
        (1, "2026-10-27", 2, "Confirmed", "暂停合作", json!("复核")),
        (
            1,
            "2026-10-30",
            4,
            "Draft",
            "优先合作",
            json!("草稿不能覆盖"),
        ),
        (3, "2026-10-30", 4, "Confirmed", "合格", json!("另一单位")),
    ] {
        insert(
            c,
            "supplier-assessments",
            json!({"supplierCompanyId":suppliers[supplier],"assessmentDate":date,"assessmentKind":"定期评价","status":state,"qualityScore":quality,"deliveryScore":3,"serviceScore":4,"priceScore":2,"averageScore":3.50,"conclusion":conclusion,"notes":notes}),
            99,
            if supplier == 3 {
                "OTHER-SUPPLIER-Q"
            } else {
                "SUPPLIER-Q"
            },
        );
    }
    let scope = DataScope {
        company_wide: true,
        company: "SUPPLIER-Q",
        user_id: 71,
        ..Default::default()
    };
    let data = c.supplier_overview(&scope).unwrap();
    assert_eq!(data["totalSuppliers"], 3);
    let items = data["items"].as_array().unwrap();
    assert_eq!(items.len(), 2);
    assert_eq!(items[0]["supplierCompanyId"], suppliers[1]);
    assert_eq!(items[1]["supplierCompanyId"], suppliers[0]);
    assert_eq!(items[1]["qualityScore"], 5);
    assert_eq!(items[1]["assessmentCount"], 3);
    assert_eq!(items[1]["latestAssessmentDate"], "2026-10-29");
    assert_eq!(items[1]["notes"], Value::Null);
    assert!(items.iter().all(|row| row.get("unusedLargeBody").is_none()));
    let own = c
        .supplier_overview(&DataScope {
            own: true,
            company: "SUPPLIER-Q",
            user_id: 71,
            ..Default::default()
        })
        .unwrap();
    assert_eq!(own["totalSuppliers"], 2);
    assert_eq!(own["items"].as_array().unwrap().len(), 1);
    assert_eq!(own["items"][0]["supplierCompanyId"], suppliers[0]);
    // Generic record projection also keeps count and ordering while dropping body fields.
    let page = c
        .query_records(&RecordQuery {
            kind: "suppliers",
            company: "SUPPLIER-Q",
            fields: &[("id", "id"), ("name", "name")],
            limit: 1,
            ..Default::default()
        })
        .unwrap();
    assert_eq!(page.0, 3);
    assert_eq!(page.1[0], json!({"id":suppliers[2],"name":"未评"}));
    insert(
        c,
        "supplier-assessments",
        json!({"supplierCompanyId":"invalid","assessmentDate":"2026-10-30","status":"Confirmed"}),
        99,
        "SUPPLIER-Q",
    );
    assert_eq!(
        c.supplier_overview(&scope).unwrap_err().kind,
        ErrorKind::Unavailable
    );
    c.rollback().unwrap();
}
