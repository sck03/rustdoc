//! Bounded durable workflow schedules; times bind as RFC 3339 instants.
use crate::{Adapter, Result, sql};
use serde_json::Value;
pub(crate) fn due(db: &(impl Adapter + ?Sized), now: &str) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let now = sql::bind(&mut v, now, pg);
    let now = sql::timestamp(&now, pg);
    let time = sql::timestamp("o.effective_at", pg);
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let select = format!(
        "SELECT {object}('requestId',o.request_id,'revision',o.revision,'actorId',o.actor_id,'contextId',o.context_id,'effectiveAt',o.effective_at,'state',o.state) AS body FROM personnel_change_orders o WHERE o.state='Pending' AND {time}<={now} ORDER BY {time},o.request_id LIMIT 20"
    );
    rows(db, select, v)
}
pub(crate) fn due_nodes(db: &(impl Adapter + ?Sized), now: &str) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let escalation = chrono::DateTime::parse_from_rfc3339(now)
        .map_err(|_| crate::Error::unavailable("流程调度时间无效。"))?
        - chrono::Duration::hours(24);
    let now = sql::bind(&mut v, now, pg);
    let now = sql::timestamp(&now, pg);
    let escalated = sql::bind(&mut v, escalation.to_rfc3339(), pg);
    let escalated = sql::timestamp(&escalated, pg);
    let time = sql::timestamp("n.due_at", pg);
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let select = format!(
        "SELECT {object}('requestId',w.request_id,'revision',w.revision,'generation',w.generation,'nodeKey',n.node_key,'dueAt',n.due_at,'level',n.reminder_level) AS body FROM workflow_instances w JOIN node_instances n ON n.instance_id=w.id AND n.generation=w.generation WHERE w.state='Pending' AND n.started_at<>'' AND n.due_at<>'' AND {time}<={now} AND (n.reminder_level=0 OR n.reminder_level=1 AND {time}<={escalated}) AND EXISTS(SELECT 1 FROM workflow_tasks t WHERE t.request_id=w.request_id AND t.submission=w.revision AND t.generation=w.generation AND t.node_key=n.node_key AND t.active=1 AND t.status='Waiting') ORDER BY {time},w.request_id LIMIT 20"
    );
    rows(db, select, v)
}
fn rows(db: &(impl Adapter + ?Sized), select: String, values: Vec<String>) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let query = if pg {
        format!("SELECT COALESCE(jsonb_agg(body),'[]'::jsonb)::text FROM ({select}) page")
    } else {
        format!("SELECT json_group_array(json(body)) FROM ({select}) page")
    };
    Ok(serde_json::from_value(db.query_json(&query, &values)?)?)
}
pub(crate) fn schedule(db: &(impl Adapter + ?Sized), entry: &Value) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let mut p = vec![];
    for (key, number) in [
        ("requestId", true),
        ("revision", true),
        ("actorId", true),
        ("contextId", false),
        ("effectiveAt", false),
    ] {
        let value = if number {
            entry[key].as_i64().unwrap_or(0).to_string()
        } else {
            entry[key].as_str().unwrap_or("").into()
        };
        let value = sql::bind(&mut v, value, pg);
        p.push(if number {
            format!("CAST({value} AS BIGINT)")
        } else {
            value
        });
    }
    db.execute_statement(&format!("INSERT INTO personnel_change_orders(request_id,revision,actor_id,context_id,effective_at,state,error_code) VALUES({},'Pending','') ON CONFLICT(request_id) DO NOTHING",p.join(",")),&v)?;
    Ok(())
}
pub(crate) fn state(db: &(impl Adapter + ?Sized), id: i64, state: &str, error: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let id = sql::bind(&mut v, id, pg);
    let state = sql::bind(&mut v, state, pg);
    let error = sql::bind(&mut v, error, pg);
    db.execute_statement(&format!("UPDATE personnel_change_orders SET state={state},error_code={error} WHERE request_id=CAST({id} AS BIGINT) AND state<>'Applied'"),&v)?;
    Ok(())
}
pub(crate) fn order(db: &(impl Adapter + ?Sized), id: i64) -> Result<Option<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let id = sql::bind(&mut v, id, pg);
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    Ok(rows(db,format!("SELECT {object}('state',state,'effectiveAt',effective_at,'errorCode',error_code) AS body FROM personnel_change_orders WHERE request_id=CAST({id} AS BIGINT)"),v)?.pop())
}
pub(crate) fn reminder(db: &(impl Adapter + ?Sized), entry: &Value, level: i64) -> Result<bool> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let instance = sql::bind(
        &mut v,
        format!("{}:{}", entry["requestId"], entry["revision"]),
        pg,
    );
    let generation = sql::bind(&mut v, entry["generation"].as_i64().unwrap_or(0), pg);
    let node = sql::bind(&mut v, entry["nodeKey"].as_str().unwrap_or(""), pg);
    let previous = sql::bind(&mut v, entry["level"].as_i64().unwrap_or(0), pg);
    let level = sql::bind(&mut v, level, pg);
    Ok(db.execute_statement(&format!("UPDATE node_instances SET reminder_level=CAST({level} AS BIGINT) WHERE instance_id={instance} AND generation=CAST({generation} AS BIGINT) AND node_key={node} AND reminder_level=CAST({previous} AS BIGINT)"),&v)? == 1)
}
