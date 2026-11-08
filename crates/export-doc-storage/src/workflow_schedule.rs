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
        "SELECT {object}('requestId',o.request_id,'revision',o.revision,'actorId',o.actor_id,'contextId',o.context_id,'effectiveAt',o.effective_at,'state',o.state) AS body FROM personnel_change_orders o JOIN workflow_instances w ON w.request_id=o.request_id AND w.revision=o.revision JOIN records r ON r.id=o.request_id AND w.revision=CAST(NULLIF(r.body->>'submissionNumber','') AS BIGINT) WHERE o.state='Pending' AND w.state='Approved' AND w.suspended_at='' AND {time}<={now} ORDER BY {time},o.request_id LIMIT 20"
    );
    rows(db, select, v)
}
pub(crate) fn due_nodes(db: &(impl Adapter + ?Sized), now: &str) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let now = sql::bind(&mut v, now, pg);
    let now = sql::timestamp(&now, pg);
    let time = sql::timestamp("n.due_at", pg);
    let next = sql::timestamp("NULLIF(n.next_escalation_at,'')", pg);
    let kind = if pg {
        "n.definition->>'kind'"
    } else {
        "json_extract(n.definition,'$.kind')"
    };
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let select = format!(
        "SELECT {object}('requestId',w.request_id,'requestKind',r.kind,'revision',w.revision,'generation',w.generation,'nodeKey',n.node_key,'dueAt',n.due_at) AS body FROM workflow_instances w JOIN records r ON r.id=w.request_id JOIN node_instances n ON n.instance_id=w.id AND n.generation=w.generation WHERE w.state IN ('Pending','Approved') AND w.suspended_at='' AND n.execution_state='Active' AND {kind} IN ('Approval','Handling') AND n.started_at<>'' AND n.due_at<>'' AND {time}<={now} AND (n.reminder_level=0 OR n.next_escalation_at<>'' AND {next}<={now}) ORDER BY {time},w.request_id LIMIT 20"
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
pub(crate) fn systems(db: &(impl Adapter + ?Sized), action: &str) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut v = vec![];
    let action = sql::bind(&mut v, action, pg);
    let field = if pg {
        "n.definition->>'systemAction'"
    } else {
        "json_extract(n.definition,'$.systemAction')"
    };
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    rows(
        db,
        format!(
            "SELECT {object}('requestId',w.request_id,'requestKind',r.kind,'revision',w.revision,'generation',w.generation) AS body FROM workflow_instances w JOIN records r ON r.id=w.request_id WHERE w.state='Approved' AND w.suspended_at='' AND EXISTS(SELECT 1 FROM node_instances n WHERE n.instance_id=w.id AND n.generation=w.generation AND n.execution_state='Active' AND {field}={action}) ORDER BY w.request_id LIMIT 20"
        ),
        v,
    )
}
pub(crate) fn exceptions(
    db: &(impl Adapter + ?Sized),
    company: &str,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let object = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let name = if pg {
        "n.definition->>'name'"
    } else {
        "json_extract(n.definition,'$.name')"
    };
    let kind = if pg {
        "n.definition->>'kind'"
    } else {
        "json_extract(n.definition,'$.kind')"
    };
    let projection = format!(
        "{object}('requestId',r.id,'requestKind',r.kind,'versionNumber',r.version,'nodeKey',n.node_key,'nodeName',{name},'nodeKind',{kind},'phase',n.phase,'state',n.execution_state,'errorCode',n.error_code,'dueAt',n.due_at)"
    );
    let source = "records r JOIN workflow_instances w ON w.request_id=r.id AND w.revision=CAST(NULLIF(r.body->>'submissionNumber','') AS BIGINT) JOIN node_instances n ON n.instance_id=w.id AND n.generation=w.generation";
    let filter = format!("r.company={company} AND n.execution_state='Blocked'");
    let select = format!(
        "SELECT {projection} AS body FROM {source} WHERE {filter} ORDER BY r.id,n.position LIMIT {} OFFSET {}",
        limit.clamp(1, 100),
        offset.max(0)
    );
    let items = if pg {
        format!("COALESCE((SELECT jsonb_agg(body) FROM ({select}) page),'[]'::jsonb)")
    } else {
        format!("json((SELECT json_group_array(json(body)) FROM ({select}) page))")
    };
    db.query_json(&format!("SELECT {object}('items',{items},'totalCount',(SELECT COUNT(*) FROM {source} WHERE {filter})){}",if pg{"::text"}else{""}),&values)
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
    db.execute_statement(&format!("UPDATE personnel_change_orders SET state={state},error_code={error} WHERE request_id=CAST({id} AS BIGINT) AND state IN ('Pending','Blocked')"),&v)?;
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
