//! Each original signed fact is read once; reused decisions are references rather than copies.
use crate::{Adapter, Result, sql};
use serde_json::Value;
pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    request: i64,
    revision: i64,
    generation: Option<i64>,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let request = sql::bind(&mut values, request, pg);
    let revision = sql::bind(&mut values, revision, pg);
    let mut filter = format!(
        "t.request_id=CAST({request} AS BIGINT) AND t.submission=CAST({revision} AS BIGINT) AND t.signature IS NOT NULL"
    );
    if let Some(generation) = generation {
        filter += &format!(
            " AND t.generation=CAST({} AS BIGINT)",
            sql::bind(&mut values, generation, pg)
        );
    }
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let signature = if pg {
        "t.signature"
    } else {
        "json(t.signature)"
    };
    let node = if pg {
        "n.definition"
    } else {
        "json(n.definition)"
    };
    let select = format!(
        "SELECT {object}('generation',t.generation,'taskKey',t.task_key,'nodeKey',t.node_key,'signature',{signature},'_node',{node}) AS body FROM workflow_tasks t JOIN workflow_instances w ON w.request_id=t.request_id AND w.revision=t.submission JOIN node_instances n ON n.instance_id=w.id AND n.generation=t.generation AND n.node_key=t.node_key WHERE {filter} ORDER BY t.generation DESC,t.stage,t.task_key LIMIT {} OFFSET {}",
        limit.clamp(1, 100),
        offset.max(0)
    );
    let items = if pg {
        format!("COALESCE((SELECT jsonb_agg(body) FROM ({select}) page),'[]'::jsonb)")
    } else {
        format!("json((SELECT json_group_array(json(body)) FROM ({select}) page))")
    };
    db.query_json(&format!("SELECT {object}('items',{items},'totalCount',(SELECT COUNT(*) FROM workflow_tasks t WHERE {filter})){}",if pg{"::text"}else{""}),&values)
}
