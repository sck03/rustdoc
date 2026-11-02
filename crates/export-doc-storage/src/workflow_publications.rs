//! Immutable definition versions and bounded, cancellable activation schedules.
use crate::{Adapter, Error, Result, sql};
use serde_json::Value;
fn object(pg: bool) -> &'static str {
    if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    }
}
fn query(db: &(impl Adapter + ?Sized), select: String, values: Vec<String>) -> Result<Value> {
    db.query_json(
        &if db.provider() == "PostgreSQL" {
            format!("SELECT value::text FROM ({select}) q(value)")
        } else {
            select
        },
        &values,
    )
}
pub(crate) fn next(db: &(impl Adapter + ?Sized), company: &str, key: &str) -> Result<i64> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let key = sql::bind(&mut values, key, pg);
    let version = sql::field("version", pg);
    let definition = sql::field("definition.key", pg);
    let value = query(
        db,
        format!(
            "SELECT {}('version',COALESCE(MAX(CAST({version} AS BIGINT)),0)) FROM record_rows r WHERE r.kind='oa-definition-version' AND r.company={company} AND {definition}={key}",
            object(pg)
        ),
        values,
    )?;
    value["version"]
        .as_i64()
        .and_then(|v| v.checked_add(1))
        .ok_or_else(|| Error::unavailable("流程版本超出范围。"))
}
pub(crate) fn versions(
    db: &(impl Adapter + ?Sized),
    company: &str,
    key: &str,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let key = sql::bind(&mut values, key, pg);
    let select = format!(
        "SELECT r.body AS body FROM record_rows r WHERE r.kind='oa-definition-version' AND r.company={company} AND {}={key} ORDER BY CAST({} AS BIGINT) DESC LIMIT {} OFFSET {}",
        sql::field("definition.key", pg),
        sql::field("version", pg),
        limit.clamp(1, 100),
        offset.max(0)
    );
    let aggregate = if pg {
        format!("COALESCE(jsonb_agg(body),'[]'::jsonb)")
    } else {
        "json_group_array(json(body))".into()
    };
    query(db, format!("SELECT {aggregate} FROM ({select}) q"), values)
}
fn projection(pg: bool) -> String {
    let enabled = if pg {
        "(expected_active_enabled=1)"
    } else {
        "json(CASE expected_active_enabled WHEN 1 THEN 'true' ELSE 'false' END)"
    };
    format!(
        "{}('id',CAST(id AS TEXT),'companyCode',company,'definitionKey',definition_key,'definitionRecordId',definition_record_id,'definitionVersion',definition_version,'expectedActiveVersion',expected_active_version,'expectedActiveEnabled',{enabled},'actorId',actor_id,'contextId',context_id,'activateAt',activate_at,'state',state,'errorCode',error_code,'versionNumber',version)",
        object(pg)
    )
}
pub(crate) fn list(
    db: &(impl Adapter + ?Sized),
    company: &str,
    due: Option<&str>,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let mut clauses = vec![];
    if !company.is_empty() {
        clauses.push(format!("company={}", sql::bind(&mut values, company, pg)));
    }
    if let Some(due) = due {
        let due = sql::bind(&mut values, due, pg);
        clauses.push(format!(
            "state='Pending' AND {}<={}",
            sql::timestamp("activate_at", pg),
            sql::timestamp(&due, pg)
        ));
    } else {
        clauses.push("state IN ('Pending','Blocked')".into());
    }
    let select = format!(
        "SELECT {} AS body FROM workflow_activations WHERE {} ORDER BY activate_at,id LIMIT 100",
        projection(pg),
        clauses.join(" AND ")
    );
    let aggregate = if pg {
        "COALESCE(jsonb_agg(body),'[]'::jsonb)"
    } else {
        "json_group_array(json(body))"
    };
    Ok(serde_json::from_value(query(
        db,
        format!("SELECT {aggregate} FROM ({select}) q"),
        values,
    )?)?)
}
pub(crate) fn save(db: &(impl Adapter + ?Sized), entry: &Value) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let mut params = vec![];
    for (key, number) in [
        ("company", false),
        ("definitionKey", false),
        ("definitionRecordId", true),
        ("definitionVersion", true),
        ("expectedActiveVersion", true),
        ("expectedActiveEnabled", true),
        ("actorId", true),
        ("contextId", false),
        ("commandKey", false),
        ("requestDigest", false),
        ("activateAt", false),
    ] {
        let value = if number {
            entry[key]
                .as_i64()
                .or_else(|| entry[key].as_bool().map(i64::from))
                .ok_or_else(|| Error::unavailable("发布调度编号缺失。"))?
                .to_string()
        } else {
            entry[key]
                .as_str()
                .ok_or_else(|| Error::unavailable("发布调度字段缺失。"))?
                .to_owned()
        };
        let value = sql::bind(&mut values, value, pg);
        params.push(if number {
            format!("CAST({value} AS BIGINT)")
        } else {
            value
        });
    }
    let result = format!("{}{}", projection(pg), if pg { "::text" } else { "" });
    let value=db.query_json(&format!("INSERT INTO workflow_activations(company,definition_key,definition_record_id,definition_version,expected_active_version,expected_active_enabled,actor_id,context_id,command_key,request_digest,activate_at,state) VALUES({},'Pending') RETURNING {result}",params.join(",")),&values)?;
    Ok(value)
}
pub(crate) fn state(
    db: &(impl Adapter + ?Sized),
    id: i64,
    expected: i64,
    state: &str,
    error: &str,
) -> Result<bool> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let id = sql::bind(&mut values, id, pg);
    let expected = sql::bind(&mut values, expected, pg);
    let state = sql::bind(&mut values, state, pg);
    let error = sql::bind(&mut values, error, pg);
    Ok(db.execute_statement(&format!("UPDATE workflow_activations SET state={state},error_code={error},version=version+1 WHERE id=CAST({id} AS BIGINT) AND version=CAST({expected} AS BIGINT) AND state IN ('Pending','Blocked')"),&values)?==1)
}
pub(crate) fn receipt(
    db: &(impl Adapter + ?Sized),
    actor: i64,
    context: &str,
    key: &str,
) -> Result<Option<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let actor = sql::bind(&mut values, actor, pg);
    let context = sql::bind(&mut values, context, pg);
    let key = sql::bind(&mut values, key, pg);
    let null = if pg { "'null'::jsonb" } else { "'null'" };
    let select = format!(
        "SELECT COALESCE((SELECT {}('companyCode',company,'definitionKey',definition_key,'requestDigest',request_digest) FROM workflow_activations WHERE actor_id=CAST({actor} AS BIGINT) AND context_id={context} AND command_key={key}),{null})",
        object(pg)
    );
    let value = query(db, select, values)?;
    Ok((!value.is_null()).then_some(value))
}
