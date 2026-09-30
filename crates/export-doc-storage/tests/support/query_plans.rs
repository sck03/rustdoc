use serde_json::json;
use std::{
    path::PathBuf,
    time::{SystemTime, UNIX_EPOCH},
};

pub fn exercise(maintenance: &str) {
    let mut client = postgres::Client::connect(maintenance, postgres::NoTls).unwrap();
    let mut tx = client.transaction().unwrap();
    tx.batch_execute(r#"SET LOCAL ROLE native_owner;
        INSERT INTO records(kind,version,owner_id,company,department,body)
        SELECT 'background-jobs',1,n%1000,'PLAN','D',jsonb_build_object('status','Succeeded','jobId',n::text,'createdAt',to_char((TIMESTAMPTZ '2026-09-30 00:00:00+00'+n*INTERVAL '1 second') AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"')) FROM generate_series(1,10000) n;
        INSERT INTO records(kind,version,owner_id,company,department,body)
        SELECT 'template-versions',1,1,'PLAN','D',jsonb_build_object('templateKind','report-templates','templateId',n%1000,'content',jsonb_build_object('versionNumber',n/1000+1)) FROM generate_series(1,10000) n;
        ANALYZE records;"#).unwrap();
    let mut plans = vec![];
    for (name, index, sql) in [
        (
            "job-owner-page",
            "records_job_owner_time",
            "SELECT id FROM records r WHERE r.kind='background-jobs' AND r.owner_id=42 ORDER BY r.body->>'createdAt' DESC,r.id DESC LIMIT 20",
        ),
        (
            "template-history-page",
            "records_template_version",
            "SELECT id FROM records r WHERE r.kind='template-versions' AND r.body->>'templateKind'='report-templates' AND CAST(r.body->>'templateId' AS BIGINT)=42 ORDER BY CAST(r.body #>> '{content,versionNumber}' AS BIGINT) DESC,r.id LIMIT 20",
        ),
    ] {
        let rows = tx
            .query(&format!("EXPLAIN (ANALYZE, BUFFERS) {sql}"), &[])
            .unwrap();
        let plan = rows
            .iter()
            .map(|row| row.get::<_, String>(0))
            .collect::<Vec<_>>()
            .join("\n");
        assert!(
            plan.contains(index),
            "{name} did not use its scoped index: {plan}"
        );
        plans.push(json!({"query":name,"plan":plan}));
    }
    tx.rollback().unwrap();
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../.codex-runtime/query-plans");
    std::fs::create_dir_all(&root).unwrap();
    let file = root.join(format!(
        "postgres-{}-{}.json",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    std::fs::write(&file, serde_json::to_vec_pretty(&plans).unwrap()).unwrap();
    println!("PostgreSQL index plan evidence: {}", file.display());
}
