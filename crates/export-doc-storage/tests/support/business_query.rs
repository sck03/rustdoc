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
    let id = c.insert(&RecordWrite::unkeyed(kind, &row)).unwrap();
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
    };
    q.offset = 0;
    assert_eq!(c.query_business(&q).unwrap().0, 2);
    q.list = BusinessList::Attachments {
        invoice_id: None,
        category_id: None,
        archived: false,
        keyword: "旧分类名称",
    };
    assert_eq!(c.query_business(&q).unwrap().0, 0);
    q.list = BusinessList::Attachments {
        invoice_id: Some(invoices[2]),
        category_id: None,
        archived: false,
        keyword: "",
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
    // Preferred versions are chosen within visibility, before bounded keyset paging.
    let mut selected = vec![];
    for index in 0..205 {
        let mut row = base.clone();
        row["invoiceNo"] = json!(format!("DASH-{index}"));
        row["type"] = json!("实际数据");
        let id = insert(c, "invoices", row.clone());
        selected.push(id);
        row["type"] = json!("报关数据");
        insert(c, "invoices", row.clone());
        row["type"] = json!("实际数据");
        row["companyScope"] = json!("OTHER");
        insert(c, "invoices", row);
    }
    let mut before = None;
    let mut actual = vec![];
    loop {
        let rows = c
            .scan_dashboard(
                &scope(),
                &[("id", "id"), ("invoiceNo", "invoiceNo")],
                before,
                None,
                "",
                200,
            )
            .unwrap();
        if rows.is_empty() {
            break;
        }
        assert!(rows.len() <= 200);
        before = rows.last().unwrap()["id"].as_i64();
        actual.extend(
            rows.into_iter()
                .filter(|r| r["invoiceNo"].as_str().unwrap().starts_with("DASH-"))
                .map(|r| r["id"].as_i64().unwrap()),
        );
    }
    selected.reverse();
    assert_eq!(actual, selected);
    let visible = insert(
        c,
        "invoices",
        json!({"companyScope":"QUERY","ownerUserId":901,"invoiceNo":"VISIBLE-VERSION","type":"报关数据","status":"Draft","versionNumber":1}),
    );
    insert(
        c,
        "invoices",
        json!({"companyScope":"QUERY","ownerUserId":902,"invoiceNo":"VISIBLE-VERSION","type":"实际数据","status":"Draft","versionNumber":1}),
    );
    insert(
        c,
        "invoices",
        json!({"companyScope":"QUERY","ownerUserId":901,"invoiceNo":"VISIBLE-VERSION","type":"实际数据","status":"Cancelled","versionNumber":1}),
    );
    let rows = c
        .scan_dashboard(
            &DataScope {
                company: "QUERY",
                user_id: 901,
                own: true,
                ..Default::default()
            },
            &[("id", "id")],
            None,
            None,
            "",
            200,
        )
        .unwrap();
    assert_eq!(
        rows[0]["id"], visible,
        "invisible or cancelled versions cannot displace an authorized invoice"
    );
    let window_scope = DataScope {
        company: "DASH-WINDOW",
        company_wide: true,
        ..Default::default()
    };
    for (number, kind, date, status) in [
        ("SAME", "报关数据", "2026-11-05", "Draft"),
        ("SAME", "实际数据", "2026-09-05", "Completed"),
        ("CURRENT", "实际数据", "2026-11-06", "Verified"),
        ("CANCELLED", "实际数据", "2026-11-06", "Cancelled"),
    ] {
        insert(
            c,
            "invoices",
            json!({"companyScope":"DASH-WINDOW", "invoiceNo":number, "type":kind, "invoiceDate":date, "status":status, "versionNumber":1}),
        );
    }
    let counts = c.dashboard_counts(&window_scope).unwrap();
    assert_eq!(
        (
            counts.total,
            counts.draft,
            counts.verified,
            counts.completed
        ),
        (2, 0, 1, 1)
    );
    let current = c
        .scan_dashboard(
            &window_scope,
            &[("invoiceNo", "invoiceNo")],
            None,
            Some(("2026-11-01", "2026-12-01")),
            "",
            200,
        )
        .unwrap();
    assert_eq!(
        current.len(),
        1,
        "select preferred version before date filtering"
    );
    assert_eq!(current[0]["invoiceNo"], "CURRENT");
    assert_eq!(
        c.scan_dashboard(
            &window_scope,
            &[("invoiceNo", "invoiceNo")],
            None,
            None,
            "Completed",
            1
        )
        .unwrap()[0]["invoiceNo"],
        "SAME"
    );
    insert(
        c,
        "suppliers",
        json!({"name":"École", "companyScope":"DASH-WINDOW", "versionNumber":1}),
    );
    insert(
        c,
        "suppliers",
        json!({"name":"Hidden", "companyScope":"OTHER", "versionNumber":1}),
    );
    assert_eq!(
        c.existing_party_names(
            "suppliers",
            &window_scope,
            &["e\u{301}cole".into(), "Hidden".into()]
        )
        .unwrap(),
        vec!["école"]
    );
    let mut candidates = (0..5000)
        .map(|index| format!("NAME-{index}"))
        .collect::<Vec<_>>();
    candidates[4999] = "e\u{301}cole".into();
    assert_eq!(
        c.existing_party_names("suppliers", &window_scope, &candidates)
            .unwrap(),
        vec!["école"]
    );
    for (name, tax, company) in [
        ("École", "A", "QUERY"),
        ("Other", "B", "QUERY"),
        ("École", "B", "OTHER"),
    ] {
        insert(
            c,
            "customers",
            json!({"customerNameEN":name,"taxId":tax,"companyScope":company,"versionNumber":1}),
        );
    }
    let candidates = c
        .party_candidates(
            "customers",
            "QUERY",
            &[("customerNameEN", "ÉCOLE".into()), ("taxId", "B".into())],
        )
        .unwrap();
    assert_eq!(candidates.len(), 2);
    assert!(candidates.iter().all(|r| r["companyScope"] == "QUERY"));
    c.rollback().unwrap();
}
