//! One read snapshot: global latest-assessment totals and bounded, filtered detail.
use crate::DataScope;
pub(crate) fn build(
    scope: &DataScope<'_>,
    filter: &str,
    offset: i64,
    limit: i64,
    pg: bool,
) -> (String, Vec<String>) {
    let mut values = vec![];
    let visible = scope.filter(&mut values, pg);
    let field = |alias: &str, key: &str| {
        if pg {
            format!("{alias}.body->>'{key}'")
        } else {
            format!("json_extract({alias}.body,'$.{key}')")
        }
    };
    let json_field = |alias: &str, key: &str| {
        if pg {
            format!("{alias}.body->'{key}'")
        } else {
            format!("json({alias}.body->'$.{key}')")
        }
    };
    let parent = field("a", "supplierCompanyId");
    let confirmed = field("a", "status");
    let date = field("a", "assessmentDate");
    let invalid = if pg {
        format!(
            "COALESCE(jsonb_typeof(a.body->'supplierCompanyId'),'')<>'number' OR COALESCE({parent},'') !~ '^-?[0-9]+$'"
        )
    } else {
        "COALESCE(json_type(a.body,'$.supplierCompanyId'),'')<>'integer'".into()
    };
    let mut pairs = vec![];
    for key in [
        "supplierCompanyId",
        "qualityScore",
        "deliveryScore",
        "serviceScore",
        "priceScore",
        "averageScore",
        "conclusion",
    ] {
        pairs.push(format!("'{key}',{}", json_field("a", key)));
    }
    for (source, target) in [
        ("name", "supplierName"),
        ("status", "supplierStatus"),
        ("category", "category"),
    ] {
        pairs.push(format!("'{target}',{}", json_field("s", source)));
    }
    pairs.push(format!(
        "'latestAssessmentDate',{}",
        json_field("a", "assessmentDate")
    ));
    pairs.push(format!(
        "'latestAssessmentKind',{}",
        json_field("a", "assessmentKind")
    ));
    pairs.push("'assessmentCount',a.assessment_count".into());
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let item = format!("{object}({})", pairs.join(","));
    let conclusion = field("a", "conclusion");
    let order = format!(
        "CASE {conclusion} WHEN '暂停合作' THEN 0 WHEN '观察' THEN 1 WHEN '合格' THEN 2 WHEN '优先合作' THEN 3 ELSE 4 END, {date} DESC, {} COLLATE {}, s.id",
        field("s", "name"),
        if pg { "\"C\"" } else { "BINARY" }
    );
    let aggregate = if pg {
        "COALESCE(jsonb_agg(item),'[]'::jsonb)"
    } else {
        "json_group_array(json(item))"
    };
    let predicate = match filter {
        "attention" => format!("{conclusion} IN ('观察','暂停合作')"),
        "preferred" => format!("{conclusion}='优先合作'"),
        _ => "1=1".into(),
    };
    let mut totals = vec!["'assessedSuppliers',(SELECT COUNT(*) FROM latest)".to_string()];
    for (key, value) in [
        ("preferredCount", "优先合作"),
        ("qualifiedCount", "合格"),
        ("watchCount", "观察"),
        ("pausedCount", "暂停合作"),
    ] {
        totals.push(format!(
            "'{key}',(SELECT COUNT(*) FROM latest a WHERE {conclusion}='{value}')"
        ));
    }
    for key in [
        "qualityScore",
        "deliveryScore",
        "serviceScore",
        "priceScore",
    ] {
        totals.push(format!(
            "'{key}',(SELECT COALESCE(SUM(CAST({} AS BIGINT)),0) FROM latest a)",
            field("a", key)
        ));
    }
    totals.push(format!(
        "'totalCount',(SELECT COUNT(*) FROM latest a WHERE {predicate})"
    ));
    let totals = totals.join(",");
    let items = format!(
        "(SELECT {aggregate} FROM (SELECT {item} AS item FROM latest a JOIN visible s ON s.id=CAST({parent} AS BIGINT) WHERE {predicate} ORDER BY {order} LIMIT {limit} OFFSET {offset}) items)"
    );
    // SQLite subqueries return SQL text; restore JSON explicitly at the parent boundary.
    let items = if pg { items } else { format!("json({items})") };
    let body = format!(
        "{object}('totalSuppliers',(SELECT COUNT(*) FROM visible),'invalidCount',(SELECT COUNT(*) FROM record_rows a WHERE a.kind='supplier-assessments' AND {confirmed}='Confirmed' AND ({invalid})),{totals},'items',{items})"
    );
    let statement = format!(
        "WITH visible AS (SELECT r.id,r.body FROM record_rows r WHERE r.kind='suppliers' AND {visible}), ranked AS (SELECT a.id,a.body,ROW_NUMBER() OVER (PARTITION BY {parent} ORDER BY {date} DESC,a.id DESC) AS row_rank,COUNT(*) OVER (PARTITION BY {parent}) AS assessment_count FROM record_rows a WHERE a.kind='supplier-assessments' AND {confirmed}='Confirmed' AND CAST({parent} AS BIGINT) IN (SELECT id FROM visible)), latest AS (SELECT * FROM ranked WHERE row_rank=1) SELECT {body}{}",
        if pg { "::text" } else { "" }
    );
    (statement, values)
}
