//! Durable command acknowledgements; values are bound and never interpolated.
use crate::{Adapter, Error, Result, sql};
use serde_json::Value;

pub(crate) fn get(
    db: &(impl Adapter + ?Sized),
    actor: i64,
    context: &str,
    key: &str,
) -> Result<Option<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let a = sql::bind(&mut values, actor, pg);
    let c = sql::bind(&mut values, context, pg);
    let k = sql::bind(&mut values, key, pg);
    let projection = if pg {
        "jsonb_build_object('command',command,'targetId',target_id,'requestDigest',request_digest,'result',result,'createdAt',created_at)"
    } else {
        "json_object('command',command,'targetId',target_id,'requestDigest',request_digest,'result',json(result),'createdAt',created_at)"
    };
    let query = if pg {
        format!(
            "SELECT COALESCE(jsonb_agg(body),'[]'::jsonb)::text FROM (SELECT {projection} AS body FROM command_receipts WHERE actor_id=CAST({a} AS BIGINT) AND context_id={c} AND command_key={k}) receipts"
        )
    } else {
        format!(
            "SELECT json_group_array(json(body)) FROM (SELECT {projection} AS body FROM command_receipts WHERE actor_id=CAST({a} AS BIGINT) AND context_id={c} AND command_key={k}) receipts"
        )
    };
    let mut result = db.query_json(&query, &values)?;
    Ok(result.as_array_mut().and_then(|values| values.pop()))
}
pub(crate) fn put(db: &(impl Adapter + ?Sized), receipt: &Value, result: &Value) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let mut p = |key: &str, number: bool| -> Result<String> {
        let value = receipt
            .get(key)
            .ok_or_else(|| Error::unavailable("命令回执字段缺失。"))?;
        let value = if number {
            value.as_i64().map(|v| v.to_string())
        } else {
            value.as_str().map(str::to_owned)
        }
        .ok_or_else(|| Error::unavailable("命令回执字段无效。"))?;
        let parameter = sql::bind(&mut values, value, pg);
        Ok(if number {
            format!("CAST({parameter} AS BIGINT)")
        } else {
            parameter
        })
    };
    let fields = vec![
        p("actorId", true)?,
        p("contextId", false)?,
        p("commandKey", false)?,
        p("command", false)?,
        p("targetId", true)?,
        p("requestDigest", false)?,
    ];
    let result = sql::bind(&mut values, serde_json::to_string(result)?, pg);
    let time = sql::bind(
        &mut values,
        receipt["createdAt"]
            .as_str()
            .ok_or_else(|| Error::unavailable("命令时间缺失。"))?,
        pg,
    );
    let result = if pg {
        format!("CAST({result} AS JSONB)")
    } else {
        result
    };
    db.execute_statement(&format!("INSERT INTO command_receipts(actor_id,context_id,command_key,command,target_id,request_digest,result,created_at) VALUES({},{result},{time})",fields.join(",")),&values)?;
    Ok(())
}
pub(crate) fn operation(db: &(impl Adapter + ?Sized), entry: &Value) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let keys = [
        "requestId",
        "submissionNumber",
        "generation",
        "actorId",
        "contextId",
        "command",
        "reason",
        "occurredAt",
    ];
    let mut parameters = vec![];
    for (index, key) in keys.iter().enumerate() {
        let value = if index < 4 {
            entry[*key].as_i64().map(|v| v.to_string())
        } else {
            entry[*key].as_str().map(str::to_owned)
        }
        .ok_or_else(|| Error::unavailable("流程操作记录无效。"))?;
        let value = sql::bind(&mut values, value, pg);
        parameters.push(if index < 4 {
            format!("CAST({value} AS BIGINT)")
        } else {
            value
        });
    }
    let details = sql::bind(&mut values, serde_json::to_string(&entry["details"])?, pg);
    parameters.push(if pg {
        format!("CAST({details} AS JSONB)")
    } else {
        details
    });
    db.execute_statement(&format!("INSERT INTO workflow_operations(request_id,submission,generation,actor_id,context_id,command,reason,occurred_at,details) VALUES({})",parameters.join(",")),&values)?;
    Ok(())
}
