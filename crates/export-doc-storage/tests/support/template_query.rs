use export_doc_storage::{
    Connection, RecordWrite, ReportTemplateQuery, TemplateAudience, TemplateVersionQuery,
};
use serde_json::{Value, json};

fn insert(c: &Connection, kind: &str, mut body: Value) -> i64 {
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
    let payload = "正文不可出现在列表".repeat(4096);
    let mut ids = Vec::new();
    for (name, owner, company, department, status, scope, kind) in [
        ("A-私有", 7, "A", "D", "Draft", "Private", "ExportDocument"),
        (
            "B-公司",
            8,
            "A",
            "X",
            "Published",
            "Company",
            "ExportDocument",
        ),
        (
            "C-部门",
            8,
            "A",
            "D",
            "Published",
            "Department",
            "ExportDocument",
        ),
        ("D-全局", 8, "B", "X", "Published", "All", "ExportDocument"),
        (
            "E-跨公司",
            8,
            "B",
            "D",
            "Published",
            "Company",
            "ExportDocument",
        ),
        (
            "F-跨部门",
            8,
            "A",
            "X",
            "Published",
            "Department",
            "ExportDocument",
        ),
        ("G-他人草稿", 8, "A", "D", "Draft", "All", "ExportDocument"),
        ("H-停用", 8, "A", "D", "Disabled", "All", "ExportDocument"),
        (
            "I-归档",
            7,
            "A",
            "D",
            "Archived",
            "Private",
            "ExportDocument",
        ),
        ("J-付款", 7, "A", "D", "Draft", "Private", "PaymentVoucher"),
        (
            "\t École %_中文\u{3000}",
            7,
            "A",
            "D",
            "Draft",
            "Private",
            "ExportDocument",
        ),
    ] {
        ids.push(insert(c, "report-templates", json!({"name":name,"ownerUserId":owner,"companyScope":company,"departmentId":department,"status":status,"shareScope":scope,"reportType":kind,"versionNumber":1,"contentHtml":payload})));
    }
    let mut q = ReportTemplateQuery {
        audience: TemplateAudience {
            user_id: 7,
            company: "A",
            department: "D",
            can_view: true,
            shared: true,
            administrator: false,
        },
        report_type: "ExportDocument",
        include_archived: false,
        keyword: "",
        exact_name: false,
        status: "",
        usable_only: false,
        offset: 0,
        limit: 2,
    };
    let (total, first) = c.query_report_templates(&q).unwrap();
    assert_eq!(total, 5);
    assert_eq!(
        first
            .iter()
            .map(|r| r["id"].as_i64().unwrap())
            .collect::<Vec<_>>(),
        ids[1..3]
    );
    assert!(first.iter().all(|r| r.get("contentHtml").is_none()));
    q.offset = 2;
    assert_eq!(c.query_report_templates(&q).unwrap().1[0]["id"], ids[3]);
    q.offset = i64::MAX;
    let (total, empty) = c.query_report_templates(&q).unwrap();
    assert_eq!(total, 5);
    assert!(empty.is_empty());
    q.offset = 0;
    q.limit = 200;
    q.usable_only = true;
    let usable = c.query_report_templates(&q).unwrap();
    assert_eq!(usable.0, 5);
    assert!(usable.1.iter().any(|row| row["id"] == ids[0]));
    assert!(
        usable
            .1
            .iter()
            .all(|row| row["id"] != ids[6] && row["id"] != ids[7] && row["id"] != ids[8])
    );
    q.audience.administrator = true;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 7);
    q.audience.administrator = false;
    q.usable_only = false;
    q.audience.shared = false;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 2);
    q.include_archived = true;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 3);
    q.keyword = "e\u{301}COLE %_";
    assert_eq!(c.query_report_templates(&q).unwrap().1[0]["id"], ids[10]);
    q.exact_name = true;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 0);
    q.keyword = "école %_中文";
    assert_eq!(c.query_report_templates(&q).unwrap().0, 1);
    q.exact_name = false;
    q.keyword = "' OR 1=1 --";
    assert_eq!(c.query_report_templates(&q).unwrap().0, 0);
    q.keyword = "";
    q.audience.can_view = false;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 0);
    q.audience.administrator = true;
    assert_eq!(c.query_report_templates(&q).unwrap().0, 10);
    q.status = "Published";
    assert_eq!(c.query_report_templates(&q).unwrap().0, 5);
    assert!(
        c.report_template_metadata(ids[0])
            .unwrap()
            .unwrap()
            .get("contentHtml")
            .is_none()
    );
    for (kind, id, number) in [
        ("report-templates", ids[0], 1),
        ("report-templates", ids[0], 2),
        ("email-templates", ids[0], 9),
        ("report-templates", ids[1], 10),
    ] {
        insert(
            c,
            "template-versions",
            json!({"templateKind":kind,"templateId":id,"versionNumber":1,"ownerUserId":7,"content":{"name":"版本","versionNumber":number,"status":"Draft","shareScope":"Private","contentHtml":payload}}),
        );
    }
    let (total, rows) = c
        .query_template_versions(&TemplateVersionQuery {
            kind: "report-templates",
            template_id: ids[0],
            offset: 0,
            limit: 1,
        })
        .unwrap();
    assert_eq!(total, 2);
    assert_eq!(rows[0]["versionNumber"], 2);
    assert!(rows[0].get("content").is_none());
    assert!(!rows[0].to_string().contains("正文"));
    let version = c
        .template_version("report-templates", ids[0], 1)
        .unwrap()
        .unwrap();
    assert_eq!(version["content"]["contentHtml"], payload);
    assert!(
        c.template_version("report-templates", ids[0], 9)
            .unwrap()
            .is_none()
    );
    for index in 0..205 {
        insert(
            c,
            "report-templates",
            json!({"name":format!("bulk-{index:03}"),"ownerUserId":7,"companyScope":"A","departmentId":"D","status":"Draft","shareScope":"Private","reportType":"ExportDocument","versionNumber":1,"contentHtml":payload}),
        );
    }
    q.keyword = "bulk-";
    q.status = "";
    q.offset = 200;
    let (total, rows) = c.query_report_templates(&q).unwrap();
    assert_eq!(total, 205);
    assert_eq!(rows.len(), 5);
    assert_eq!(rows[0]["name"], "bulk-200");
    assert!(serde_json::to_vec(&rows).unwrap().len() < 2048);
    c.rollback().unwrap();
}
