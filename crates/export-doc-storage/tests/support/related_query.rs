use export_doc_storage::{Connection, RecordWrite, RelatedQuery};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, mut body: Value, owner: i64) -> i64 {
    body["versionNumber"] = json!(1);
    body["ownerUserId"] = json!(owner);
    body["companyScope"] = json!("RELATED");
    body["departmentId"] = json!("D");
    let id = c.insert(&RecordWrite::unkeyed(kind, &body)).unwrap();
    body["id"] = json!(id);
    c.set_body(id, &body).unwrap();
    id
}
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let mut expected = vec![];
    for index in 0..203 {
        let id = insert(
            c,
            "crm-contacts",
            json!({"crmCustomerId":7001,"name":format!("联系人{index}"),"isPrimary":index==0,"privateMarker":"hidden"}),
            1,
        );
        expected.push(id);
    }
    insert(
        c,
        "crm-contacts",
        json!({"crmCustomerId":7002,"name":"other","isPrimary":true}),
        2,
    );
    let fields = [("id", "id"), ("name", "name")];
    let query = RelatedQuery {
        kind: "crm-contacts",
        parent_id: 7001,
        include_other_drafts: false,
        user_id: 1,
        fields: &fields,
        offset: 0,
        limit: 2,
    };
    let (total, rows) = c.query_related(&query).unwrap();
    assert_eq!(total, 203);
    assert_eq!(rows.len(), 2);
    assert_eq!(rows[0]["id"], expected[0]);
    assert_eq!(rows[1]["id"], *expected.last().unwrap());
    assert!(rows.iter().all(|row| row.get("privateMarker").is_none()));
    let mut lookup_ids = expected.clone();
    lookup_ids.extend_from_slice(&expected);
    lookup_ids.push(i64::MAX);
    let indexed = c
        .records_by_ids("crm-contacts", &lookup_ids, &fields)
        .unwrap();
    assert_eq!(indexed.len(), expected.len());
    assert_eq!(
        indexed
            .iter()
            .map(|row| row["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        expected.iter().rev().copied().collect::<Vec<_>>()
    );
    assert!(indexed.iter().all(|row| row.get("privateMarker").is_none()));
    let (_, next) = c
        .query_related(&RelatedQuery { offset: 2, ..query })
        .unwrap();
    assert_eq!(next[0]["id"], expected[201]);

    for (status, owner) in [("Confirmed", 2), ("Draft", 1), ("Draft", 2)] {
        insert(
            c,
            "supplier-assessments",
            json!({"supplierCompanyId":7001,"status":status,"assessmentDate":"2026-10-25"}),
            owner,
        );
    }
    let assessments = RelatedQuery {
        kind: "supplier-assessments",
        parent_id: 7001,
        include_other_drafts: false,
        user_id: 1,
        fields: &fields,
        offset: 0,
        limit: 200,
    };
    assert_eq!(c.query_related(&assessments).unwrap().0, 2);
    assert_eq!(
        c.query_related(&RelatedQuery {
            include_other_drafts: true,
            ..assessments
        })
        .unwrap()
        .0,
        3
    );

    let product = insert(
        c,
        "products",
        json!({"productCode":"P-1","nameCN":"商品","nameEN":"Product"}),
        1,
    );
    insert(
        c,
        "supplier-products",
        json!({"supplierCompanyId":7001,"productId":product,"referencePrice":123.45678}),
        1,
    );
    let fields = [
        ("productCode", "productCode"),
        ("productNameCN", "productNameCN"),
        ("referencePrice", "referencePrice"),
        ("__linkedProductId", "__linkedProductId"),
    ];
    let (_, rows) = c
        .query_related(&RelatedQuery {
            kind: "supplier-products",
            parent_id: 7001,
            include_other_drafts: false,
            user_id: 1,
            fields: &fields,
            offset: 0,
            limit: 1,
        })
        .unwrap();
    assert_eq!(rows[0]["productCode"], "P-1");
    assert_eq!(rows[0]["productNameCN"], "商品");
    assert_eq!(rows[0]["referencePrice"], json!(123.45678));
    assert_eq!(rows[0]["__linkedProductId"], product);
    c.rollback().unwrap();
}
