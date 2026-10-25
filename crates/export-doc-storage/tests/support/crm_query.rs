use export_doc_storage::{Connection, CrmList, CrmQuery, DataScope, RecordWrite};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, mut body: Value) -> i64 {
    for (key, value) in [
        ("companyScope", json!("CRM")),
        ("departmentId", json!("A")),
        ("ownerUserId", json!(901)),
        ("versionNumber", json!(1)),
    ] {
        if body.get(key).is_none() {
            body[key] = value;
        }
    }
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

/// The exact same count, page, scope, time and projection contract runs on both providers.
pub fn exercise(c: &Connection) {
    c.begin().unwrap();
    let own = || DataScope {
        company: "CRM",
        own: true,
        user_id: 901,
        ..Default::default()
    };
    let company = || DataScope {
        company: "CRM",
        company_wide: true,
        ..Default::default()
    };
    let customer = insert(
        c,
        "crm-customers",
        json!({"name":"École 客户","large":"unused".repeat(8000)}),
    );
    let hidden = insert(
        c,
        "crm-customers",
        json!({"name":"隐藏客户","ownerUserId":902}),
    );
    let contact = insert(
        c,
        "crm-contacts",
        json!({"crmCustomerId":customer,"name":"联系人"}),
    );
    let product = insert(
        c,
        "products",
        json!({"productCode":"PX-01","nameCN":"","nameEN":"Café Coat"}),
    );
    let mut ids = vec![];
    for (party, due, completed, summary) in [
        (customer, Some("2026-10-25T08:30:00+08:00"), false, "later"),
        (
            customer,
            Some("2026-10-25T01:00:00+02:00"),
            false,
            "earlier",
        ),
        (customer, None, false, "undated"),
        (customer, Some("2026-10-24T00:00:00Z"), true, "completed"),
        (hidden, Some("2026-10-23T00:00:00Z"), false, "hidden"),
    ] {
        ids.push(insert(c, "crm-follow-ups", json!({"crmCustomerId":party,"crmContactId":contact,"summary":summary,"isCompleted":completed,"followedUpAt":"2026-10-20T00:00:00Z","nextFollowUpAt":due,"large":"unused".repeat(8000)})));
    }
    let fields = [
        ("id", "id"),
        ("summary", "summary"),
        ("customerName", "customerName"),
        ("contactName", "contactName"),
    ];
    let mut q = CrmQuery {
        list: CrmList::FollowUps {
            include_completed: false,
            customer_id: None,
            record_id: None,
        },
        scope: company(),
        customer_scope: own(),
        fields: &fields,
        offset: 0,
        limit: 1,
    };
    let (count, rows) = c.query_crm(&q).unwrap();
    assert_eq!(count, 3);
    assert_eq!(rows[0]["summary"], "earlier");
    assert_eq!(rows[0]["customerName"], "École 客户");
    assert_eq!(rows[0]["contactName"], "联系人");
    assert!(serde_json::to_vec(&rows).unwrap().len() < 300);
    q.offset = 1;
    assert_eq!(c.query_crm(&q).unwrap().1[0]["summary"], "later");
    q.offset = 2;
    assert_eq!(c.query_crm(&q).unwrap().1[0]["summary"], "undated");
    q.offset = i64::MAX;
    assert_eq!(c.query_crm(&q).unwrap().0, 3);
    assert!(c.query_crm(&q).unwrap().1.is_empty());
    q.offset = 0;
    q.list = CrmList::FollowUps {
        include_completed: true,
        customer_id: Some(customer),
        record_id: Some(ids[3]),
    };
    assert_eq!(c.query_crm(&q).unwrap().0, 1);
    for (summary, due) in [
        ("nano-earlier", "2026-10-25T00:30:00.000000100Z"),
        ("nano-later", "2026-10-25T08:30:00.000000200+08:00"),
    ] {
        insert(
            c,
            "crm-follow-ups",
            json!({"crmCustomerId":customer,"crmContactId":null,"summary":summary,"isCompleted":false,"followedUpAt":"2026-10-20T00:00:00Z","nextFollowUpAt":due}),
        );
    }
    q.list = CrmList::FollowUps {
        include_completed: false,
        customer_id: Some(customer),
        record_id: None,
    };
    q.offset = 2;
    assert_eq!(c.query_crm(&q).unwrap().1[0]["summary"], "nano-earlier");
    q.offset = 3;
    assert_eq!(c.query_crm(&q).unwrap().1[0]["summary"], "nano-later");
    q.offset = 0;
    q.limit = 2;
    let scan = c.scan_crm(&q, 0).unwrap();
    assert_eq!(scan.len(), 2);
    let cursor = scan.last().unwrap()["id"].as_i64().unwrap();
    let following = c.scan_crm(&q, cursor).unwrap();
    assert!(
        following
            .iter()
            .all(|row| row["id"].as_i64().unwrap() > cursor)
    );
    q.limit = 1;
    q.list = CrmList::Contacts;
    q.scope = DataScope::default();
    assert_eq!(
        c.query_crm(&q).unwrap().0,
        0,
        "contact permissions remain independent"
    );
    q.scope = company();
    assert_eq!(c.query_crm(&q).unwrap().0, 1);
    let fields = [
        ("id", "id"),
        ("title", "title"),
        ("customerName", "customerName"),
        ("productCode", "productCode"),
        ("linkedProductId", "linkedProductId"),
        ("productName", "productName"),
        ("estimatedAmount", "estimatedAmount"),
    ];
    q.fields = &fields;
    for (party, title, stage, archived) in [
        (customer, "open", "线索", false),
        (customer, "closed", "已成交", false),
        (customer, "archived", "线索", true),
        (hidden, "hidden", "线索", false),
    ] {
        let mut row = json!({"crmCustomerId":party,"productId":product,"title":title,"stage":stage,"isArchived":archived,"quotationNo":"Q-01","updatedAt":"2026-10-25T00:00:00Z","expectedCloseDate":"2026-10-28","large":"unused".repeat(8000)});
        row["estimatedAmount"] = serde_json::from_str("1234567890.123456789").unwrap();
        insert(c, "opportunities", row);
    }
    q.list = CrmList::Opportunities {
        customer_id: None,
        stage: "",
        keyword: "E\u{301}cole",
        upcoming_closings: false,
    };
    let (count, rows) = c.query_crm(&q).unwrap();
    assert_eq!(count, 2);
    assert_eq!(rows[0]["title"], "open");
    assert_eq!(rows[0]["productCode"], "PX-01");
    assert_eq!(rows[0]["linkedProductId"], product);
    assert_eq!(rows[0]["productName"], "Café Coat");
    assert_eq!(
        rows[0]["estimatedAmount"].to_string(),
        "1234567890.123456789"
    );
    assert!(!rows[0].to_string().contains("unused"));
    for keyword in ["cafe\u{301}", "PX-01"] {
        q.list = CrmList::Opportunities {
            customer_id: Some(customer),
            stage: "线索",
            keyword,
            upcoming_closings: false,
        };
        assert_eq!(c.query_crm(&q).unwrap().0, 1);
    }
    q.list = CrmList::Opportunities {
        customer_id: None,
        stage: "",
        keyword: "",
        upcoming_closings: true,
    };
    assert_eq!(c.query_crm(&q).unwrap().0, 1);
    let benchmark_customer = insert(c, "crm-customers", json!({"name":"SCALE customer"}));
    let large = "unused-crm-detail".repeat(256);
    for number in 0..1000 {
        insert(
            c,
            "crm-follow-ups",
            json!({"crmCustomerId":benchmark_customer,"summary":format!("SCALE-{number:04}"),"isCompleted":false,
            "followedUpAt":"2026-10-25T00:00:00Z","nextFollowUpAt":null,"large":large}),
        );
        insert(
            c,
            "opportunities",
            json!({"crmCustomerId":benchmark_customer,"title":format!("SCALE-{number:04}"),"stage":"线索","isArchived":false,
            "updatedAt":"2026-10-25T00:00:00Z","estimatedAmount":1,"large":large}),
        );
    }
    q.limit = 20;
    for (label, list) in [
        (
            "follow-ups",
            CrmList::FollowUps {
                include_completed: false,
                customer_id: Some(benchmark_customer),
                record_id: None,
            },
        ),
        (
            "opportunities",
            CrmList::Opportunities {
                customer_id: Some(benchmark_customer),
                stage: "",
                keyword: "SCALE",
                upcoming_closings: false,
            },
        ),
    ] {
        q.list = list;
        let started = std::time::Instant::now();
        let (count, page) = c.query_crm(&q).unwrap();
        let payload = serde_json::to_vec(&page).unwrap();
        assert_eq!(count, 1000);
        assert_eq!(page.len(), 20);
        assert!(payload.len() < 8192);
        assert!(
            !String::from_utf8(payload.clone())
                .unwrap()
                .contains("unused-crm-detail")
        );
        println!(
            "{} CRM {label} query: rows=1000/page=20/payload={} bytes/elapsed={}us",
            c.provider(),
            payload.len(),
            started.elapsed().as_micros()
        );
    }
    c.rollback().unwrap();
}
