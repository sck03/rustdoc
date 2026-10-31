//! Relational restrictions use stable member references and bounded sets.
use crate::{Adapter, Error, ErrorKind, Result, sql};
use serde_json::Value;
pub(crate) fn list(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    id: i64,
    resolved: bool,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let kind = sql::bind(&mut values, kind, pg);
    let id = sql::bind(&mut values, id, pg);
    let obj = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let member = if resolved {
        format!(
            "{obj}('unitCode',COALESCE(u.code,du.code),'departmentCode',d.code,'projectId',CAST(p.id AS TEXT))"
        )
    } else {
        format!("{obj}('id',CAST(COALESCE(m.unit_id,m.department_id,m.project_id) AS TEXT))")
    };
    let member_rows = format!(
        "SELECT {member} AS body FROM access_scope_members m LEFT JOIN organization_units u ON u.id=m.unit_id LEFT JOIN organization_departments d ON d.id=m.department_id LEFT JOIN organization_units du ON du.id=d.unit_id LEFT JOIN project_scopes p ON p.id=m.project_id WHERE m.restriction_id=r.id ORDER BY m.unit_id,m.department_id,m.project_id"
    );
    let members = if pg {
        format!("(SELECT COALESCE(jsonb_agg(body),'[]'::jsonb) FROM ({member_rows}) members)")
    } else {
        format!("json((SELECT json_group_array(json(body)) FROM ({member_rows}) members))")
    };
    let select = format!(
        "SELECT {obj}('id',CAST(r.id AS TEXT),'resourceKey',r.resource_key,'action',r.action,'mode',r.mode,'reason',r.reason,'versionNumber',r.version,'members',{members}) AS body FROM access_restrictions r WHERE principal_kind={kind} AND principal_id=CAST({id} AS BIGINT) ORDER BY r.resource_key,r.action LIMIT 1000"
    );
    let query = if pg {
        format!("SELECT COALESCE(jsonb_agg(body),'[]'::jsonb)::text FROM ({select}) page")
    } else {
        format!("SELECT json_group_array(json(body)) FROM ({select}) page")
    };
    Ok(serde_json::from_value(db.query_json(&query, &values)?)?)
}
pub(crate) fn save(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    id: i64,
    body: &Value,
) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let mut bind = |v: String| sql::bind(&mut values, v, pg);
    let p = vec![
        bind(kind.into()),
        format!("CAST({} AS BIGINT)", bind(id.to_string())),
        bind(body["resourceKey"].as_str().unwrap_or("").into()),
        bind(body["action"].as_str().unwrap_or("").into()),
        bind(body["mode"].as_str().unwrap_or("").into()),
        bind(body["reason"].as_str().unwrap_or("").into()),
    ];
    let expected = body["expectedVersion"].as_i64().unwrap_or(0);
    let existing = list(db, kind, id, false)?
        .into_iter()
        .find(|r| r["resourceKey"] == body["resourceKey"] && r["action"] == body["action"]);
    let row = if let Some(existing) = existing {
        if existing["versionNumber"] != expected {
            return Err(Error::new(ErrorKind::Conflict, "授权限制版本已改变。"));
        }
        let mut update = vec![];
        let mode = sql::bind(&mut update, body["mode"].as_str().unwrap_or(""), pg);
        let reason = sql::bind(&mut update, body["reason"].as_str().unwrap_or(""), pg);
        let key = sql::bind(&mut update, existing["id"].as_str().unwrap_or(""), pg);
        db.execute_statement(&format!("UPDATE access_restrictions SET mode={mode},reason={reason},version=version+1 WHERE id=CAST({key} AS BIGINT)"),&update)?;
        existing["id"]
            .as_str()
            .unwrap_or("")
            .parse::<i64>()
            .map_err(|_| Error::unavailable("授权限制编号无效。"))?
    } else {
        if expected != 0 {
            return Err(Error::new(ErrorKind::Conflict, "新建限制不能携带旧版本。"));
        }
        let obj = if pg {
            "jsonb_build_object"
        } else {
            "json_object"
        };
        let result=db.query_json(&format!("INSERT INTO access_restrictions(principal_kind,principal_id,resource_key,action,mode,reason,version) VALUES({},1) RETURNING {obj}('id',CAST(id AS TEXT)){}",p.join(","),if pg{"::text"}else{""}),&values)?;
        result["id"]
            .as_str()
            .unwrap_or("")
            .parse::<i64>()
            .map_err(|_| Error::unavailable("授权限制编号无效。"))?
    };
    let mut values = vec![];
    let row_key = sql::bind(&mut values, row, pg);
    db.execute_statement(
        &format!("DELETE FROM access_scope_members WHERE restriction_id=CAST({row_key} AS BIGINT)"),
        &values,
    )?;
    let column = match body["mode"].as_str() {
        Some("Units") => Some("unit_id"),
        Some("Departments") => Some("department_id"),
        Some("Projects") => Some("project_id"),
        _ => None,
    };
    if let Some(column) = column {
        for member in body["members"].as_array().into_iter().flatten() {
            let mut values = vec![];
            let r = sql::bind(&mut values, row, pg);
            let m = sql::bind(&mut values, member.as_str().unwrap_or(""), pg);
            db.execute_statement(&format!("INSERT INTO access_scope_members(restriction_id,{column}) VALUES(CAST({r} AS BIGINT),CAST({m} AS BIGINT))"),&values)?;
        }
    }
    list(db, kind, id, false)?
        .into_iter()
        .find(|r| r["id"] == row.to_string())
        .ok_or_else(|| Error::unavailable("限制未能回读。"))
}
