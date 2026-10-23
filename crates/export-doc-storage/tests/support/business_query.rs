use export_doc_storage::{BusinessList, BusinessQuery, Connection, DataScope, RecordWrite};
use serde_json::{Value, json};
fn insert(c: &Connection, kind: &str, mut row: Value) -> i64 {
    if row.get("ownerUserId").is_none() {
        row["ownerUserId"] = json!(901);
    }
    if row.get("companyScope").is_none() {
        row["companyScope"] = json!("QUERY");
    }
    if row.get("departmentId").is_none() {
        row["departmentId"] = json!("A");
    }
    let id = c
        .insert(&RecordWrite {
            kind,
            identity: None,
            body: &row,
        })
        .unwrap();
    row["id"] = json!(id);
    c.set_body(id, &row).unwrap();
    id
}
// Runs unchanged against SQLite and real PostgreSQL 18.
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let fields = [("id", "id"), ("amount", "totalAmount")];
    let base = json!({"ownerUserId":901,"companyScope":"QUERY","departmentId":"A","status":"Draft","versionNumber":1});
    let mut invoices = vec![];
    for (number, company, po, name) in [
        ("A12", "QUERY", "PO123", "École 中文"),
        ("B12", "QUERY", "XPO123", "OTHER"),
        ("A12", "OTHER", "PO123", "École 中文"),
    ] {
        let mut row = base.clone();
        row["companyScope"] = json!(company);
        row["invoiceNo"] = json!(number);
        row["invoiceDate"] = json!("2026-10-23");
        row["shipmentDate"] = json!("2026-10-24");
        row["items"] =
            json!([{"poNumber":po,"styleName":name,"styleNo":"ABC123","hsCode":"12345"}]);
        row["totalAmount"] = serde_json::from_str("1234567890.123456789").unwrap();
        row["largeBody"] = json!("unused".repeat(4096));
        invoices.push(insert(c, "invoices", row));
    }
    let scope = || DataScope {
        company: "QUERY",
        company_wide: true,
        ..Default::default()
    };
    let filter =
        json!({"startDate":"2026-10-01","endDateExclusive":"2026-11-01","styleName":"école"});
    let tokens = vec!["po12".into()];
    let mut q = BusinessQuery {
        list: BusinessList::Invoices {
            filter: &filter,
            tokens: &tokens,
        },
        scope: scope(),
        fields: &fields,
        offset: 0,
        limit: 1,
    };
    let (total, rows) = c.query_business(&q).unwrap();
    assert_eq!(total, 1);
    assert_eq!(rows[0]["id"], invoices[0]);
    assert_eq!(rows[0]["amount"].to_string(), "1234567890.123456789");
    assert!(!rows[0].to_string().contains("unused"));
    q.offset = i64::MAX;
    assert!(c.query_business(&q).unwrap().1.is_empty());
    let category = insert(
        c,
        "attachment-categories",
        json!({"name":"改名后的分类","versionNumber":1,"companyScope":"QUERY"}),
    );
    for invoice in &invoices {
        insert(
            c,
            "attachments",
            json!({"invoiceId":invoice,"categoryId":category,"categoryName":"旧分类名称","title":"报价资料","isArchived":false,"versionNumber":1}),
        );
    }
    q.list = BusinessList::Attachments {
        invoice_id: None,
        category_id: None,
        archived: false,
        keyword: "改名后的分类",
        status: "",
    };
    q.offset = 0;
    assert_eq!(c.query_business(&q).unwrap().0, 2);
    q.list = BusinessList::Attachments {
        invoice_id: None,
        category_id: None,
        archived: false,
        keyword: "旧分类名称",
        status: "",
    };
    assert_eq!(c.query_business(&q).unwrap().0, 0);
    q.list = BusinessList::Attachments {
        invoice_id: Some(invoices[2]),
        category_id: None,
        archived: false,
        keyword: "",
        status: "",
    };
    assert_eq!(c.query_business(&q).unwrap().0, 0);
    for code in ["B", "A"] {
        let mut row = base.clone();
        row["productCode"] = json!(code);
        row["nameCN"] = json!("école");
        insert(c, "products", row);
    }
    q.list = BusinessList::Products {
        keyword: "E\u{301}COLE",
    };
    q.fields = &[("productCode", "productCode")];
    let (total, rows) = c.query_business(&q).unwrap();
    assert_eq!(total, 2);
    assert_eq!(rows[0]["productCode"], "A");
    q.list = BusinessList::InvoiceIds(&invoices);
    q.fields = &[("id", "id")];
    assert_eq!(c.query_business(&q).unwrap().0, 2);
    let large = "unused-product-detail".repeat(400);
    for number in 0..1000 {
        let mut row = base.clone();
        row["productCode"] = json!(format!("SCALE-{number:04}"));
        row["nameCN"] = json!("规模验证");
        row["details"] = json!(large);
        insert(c, "products", row);
    }
    q.list = BusinessList::Products { keyword: "SCALE-" };
    q.fields = &[("productCode", "productCode")];
    q.limit = 20;
    let started = std::time::Instant::now();
    let (count, page) = c.query_business(&q).unwrap();
    assert_eq!(count, 1000);
    assert_eq!(page.len(), 20);
    assert!(serde_json::to_vec(&page).unwrap().len() < 2048);
    println!(
        "{} specialized product query: rows=1000/page=20/payload={} bytes/elapsed={}us",
        c.provider(),
        serde_json::to_vec(&page).unwrap().len(),
        started.elapsed().as_micros()
    );
    c.rollback().unwrap();
}
