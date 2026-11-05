//! Plans and timings for the same scoped SQL used by application reads.
use export_doc_storage::{
    Connection, DataScope, GenericQuery, HsQuery, JobQuery, QueryPlan, RecordWrite,
    TemplateVersionQuery,
};
use serde_json::{Value, json};
use std::time::Instant;

pub fn save(c: &Connection, name: &str, evidence: &Value) {
    let file =
        crate::test_workspace::create("query-plans").join(format!("{}-{name}.json", c.provider()));
    std::fs::write(&file, serde_json::to_vec_pretty(evidence).unwrap()).unwrap();
    println!("Production query evidence: {}", file.display());
}
fn insert(c: &Connection, kind: &str, mut body: Value) {
    let id = c.insert(&RecordWrite::unkeyed(kind, &body)).unwrap();
    body["id"] = json!(id);
    c.set_body(id, &body).unwrap();
}
pub fn exercise(c: &Connection) {
    let rows: usize = std::env::var("EXPORTDOC_QUERY_PLAN_ROWS")
        .ok()
        .map(|v| v.parse().unwrap())
        .unwrap_or(1000);
    assert!((1000..=100000).contains(&rows));
    c.begin().unwrap();
    let start = Instant::now();
    let mut input_bytes = 0;
    for n in 0..rows {
        let mut body = json!({"versionNumber":1,"ownerUserId":n%100,"companyScope":if n%2==0 {"PLAN"} else {"OTHER"},"departmentId":if n%10==0 {"D"} else {"X"},"invoiceNo":format!("INV-{n:05}"),"status":"Draft","name":format!("上海外贸 CAFE\u{301} marker-{n:05}"),"items":[]});
        if n % 100 == 0 {
            body["padding"] = json!(format!("oversize-body{}", "x".repeat(65537)));
        }
        input_bytes += serde_json::to_vec(&body).unwrap().len();
        insert(c, "invoices", body.clone());
        if n < 1000 {
            body["code"] = json!(format!("{n:010}"));
            body["status"] = json!("Active");
            body["effectiveYear"] = json!(2026);
            insert(c, "hs-codes", body.clone());
            body["status"] = json!("Succeeded");
            body["createdAt"] = json!(format!("2026-11-01T00:{:02}:{:02}Z", n / 60, n % 60));
            body["jobId"] = json!(format!("plan-job-{n}"));
            insert(c, "background-jobs", body.clone());
            body["templateKind"] = json!("report-templates");
            body["templateId"] = json!(n % 100);
            body["content"] = json!({"versionNumber":n/100+1});
            insert(c, "template-versions", body);
        }
    }
    let write_ms = start.elapsed().as_secs_f64() * 1000.0;
    let fields = [
        ("id", "id"),
        ("invoiceNo", "invoiceNo"),
        ("status", "status"),
    ];
    let mut q = GenericQuery {
        kind: "invoices",
        scope: DataScope {
            company_wide: true,
            company: "PLAN",
            ..Default::default()
        },
        fields: &fields,
        limit: 20,
        ..Default::default()
    };
    let mut plans = vec![];
    for keyword in [
        "",
        "贸",
        "外贸",
        "café",
        "CAFE\u{301}",
        "marker-00042",
        "\"status\":\"draft\"",
        "oversize-body",
        "' OR 1=1 --",
        "%_",
    ] {
        q.keyword = keyword;
        let (count, page) = c.query_generic(&q).unwrap();
        let expected = match keyword {
            "marker-00042" => 1,
            "oversize-body" => rows.div_ceil(100),
            "' OR 1=1 --" | "%_" => 0,
            _ => rows.div_ceil(2),
        };
        assert_eq!(count, expected as i64, "{keyword}");
        assert_eq!(page.len(), expected.min(20));
        let mut plan = c.explain_query(&QueryPlan::Generic(&q)).unwrap();
        plan["name"] = json!(format!("invoice-search-{keyword}"));
        plan["count"] = json!(count);
        plans.push(plan);
    }
    q.keyword = "";
    for own in [false, true] {
        q.scope = DataScope {
            company: "PLAN",
            department: "D",
            user_id: 42,
            own,
            department_wide: !own,
            ..Default::default()
        };
        let expected = if own {
            (rows + 57) / 100
        } else {
            rows.div_ceil(10)
        };
        assert_eq!(c.query_generic(&q).unwrap().0, expected as i64);
        plans.push(c.explain_query(&QueryPlan::Generic(&q)).unwrap());
    }
    for status in ["", "Succeeded"] {
        let q = JobQuery {
            owner: Some(42),
            context: None,
            active: None,
            status,
            keyword: "",
            retention: None,
            fields: &fields,
            offset: 0,
            limit: 20,
        };
        assert_eq!(c.query_jobs(&q).unwrap().0, 10);
        plans.push(c.explain_query(&QueryPlan::Jobs(&q)).unwrap());
    }
    let q = TemplateVersionQuery {
        kind: "report-templates",
        template_id: 42,
        offset: 0,
        limit: 20,
    };
    assert_eq!(c.query_template_versions(&q).unwrap().0, 10);
    plans.push(c.explain_query(&QueryPlan::TemplateVersions(&q)).unwrap());
    for year in [None, Some(2026)] {
        let q = HsQuery {
            kind: "hs-codes",
            current_year: year,
            prefix: "000000004",
            ranked_examples: false,
            fields: &[("code", "code")],
            offset: 0,
            limit: 20,
        };
        let actual = c.query_hs(&q).unwrap();
        assert_eq!(actual.len(), 10);
        assert_eq!(actual[0]["code"], "0000000040");
        let plan = c.explain_query(&QueryPlan::Hs(&q)).unwrap();
        assert!(
            plan["plan"].to_string().contains("records_hs_"),
            "HS prefix must seek its existing index: {plan}"
        );
        plans.push(plan);
    }
    c.rollback().unwrap();
    save(
        c,
        "lists",
        &json!({"schema":export_doc_storage::SCHEMA_VERSION,"invoiceRows":rows,"additionalRows":3000,"inputBytes":input_bytes,"transactionalWriteMs":write_ms,"plans":plans}),
    );
}
