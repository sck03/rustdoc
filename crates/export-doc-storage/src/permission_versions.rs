//! Drafts never change published grants. Each identity binds an explicit version.
use crate::{Adapter, Error, ErrorKind, Result, sql};
use serde_json::{Value, json};
struct Statement {
    pg: bool,
    values: Vec<String>,
}
impl Statement {
    fn new(db: &(impl Adapter + ?Sized)) -> Self {
        Self {
            pg: db.provider() == "PostgreSQL",
            values: vec![],
        }
    }
    fn text(&mut self, value: impl ToString) -> String {
        sql::bind(&mut self.values, value, self.pg)
    }
    fn number(&mut self, value: impl ToString) -> String {
        format!("CAST({} AS BIGINT)", self.text(value))
    }
    fn query(self, db: &(impl Adapter + ?Sized), query: String) -> Result<Value> {
        db.query_json(&query, &self.values)
    }
}
fn object(pg: bool) -> &'static str {
    if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    }
}
pub(crate) fn publication(
    db: &(impl Adapter + ?Sized),
    scheme: i64,
    version: i64,
) -> Result<Option<Value>> {
    let mut s = Statement::new(db);
    let id = s.number(scheme);
    let v = s.number(version);
    let column = if s.pg { "body::text" } else { "body" };
    let mut value=s.query(db,format!("SELECT COALESCE((SELECT {column} FROM permission_scheme_versions WHERE scheme_id={id} AND version={v}),'null')"))?;
    if value.is_null() {
        return Ok(None);
    }
    value["versionNumber"] = json!(version);
    Ok(Some(value))
}
pub(crate) fn versions(
    db: &(impl Adapter + ?Sized),
    scheme: i64,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let mut s = Statement::new(db);
    let id = s.number(scheme);
    let obj = object(s.pg);
    let offset = offset.max(0);
    let limit = limit.clamp(1, 100);
    let select = format!(
        "SELECT {obj}('schemeId',CAST(scheme_id AS TEXT),'version',version,'publishedAt',published_at) AS body FROM permission_scheme_versions WHERE scheme_id={id} ORDER BY version DESC LIMIT {limit} OFFSET {offset}"
    );
    let array = if s.pg {
        format!("(SELECT COALESCE(jsonb_agg(body),'[]'::jsonb) FROM ({select}) page)")
    } else {
        format!("json((SELECT json_group_array(json(body)) FROM ({select}) page))")
    };
    let query = format!(
        "SELECT {obj}('items',{array},'totalCount',(SELECT COUNT(*) FROM permission_scheme_versions WHERE scheme_id={id}),'pageNumber',{},'pageSize',{limit}){}",
        offset / limit + 1,
        if s.pg { "::text" } else { "" }
    );
    s.query(db, query)
}
pub(crate) fn publish(
    db: &(impl Adapter + ?Sized),
    scheme: i64,
    body: &Value,
    actor: i64,
    time: &str,
) -> Result<i64> {
    let next = versions(db, scheme, 0, 1)?["items"]
        .as_array()
        .and_then(|items| items.first())
        .and_then(|item| item["version"].as_i64())
        .unwrap_or(0)
        + 1;
    if next > 2000 {
        return Err(Error::new(
            ErrorKind::Busy,
            "权限方案发布版本已达容量，请建立新的明确方案。",
        ));
    }
    let mut snapshot = body.clone();
    if let Some(fields) = snapshot.as_object_mut() {
        for field in [
            "expectedVersion",
            "versionNumber",
            "publishedVersion",
            "accountReferenceCount",
            "unitRoleReferenceCount",
            "effectiveGrants",
            "__commandReceipt",
        ] {
            fields.remove(field);
        }
    }
    let mut s = Statement::new(db);
    let id = s.number(scheme);
    let v = s.number(next);
    let body = s.text(serde_json::to_string(&snapshot)?);
    let body = if s.pg {
        format!("CAST({body} AS JSONB)")
    } else {
        body
    };
    let time = s.text(time);
    let actor = if actor > 0 {
        s.number(actor)
    } else {
        "NULL".into()
    };
    db.execute_statement(&format!("INSERT INTO permission_scheme_versions(scheme_id,version,body,published_at,published_by) VALUES({id},{v},{body},{time},{actor})"),&s.values)?;
    Ok(next)
}
pub(crate) fn sync(db: &(impl Adapter + ?Sized), id: i64, kind: &str, body: &Value) -> Result<()> {
    if kind == "permission-templates" {
        if body["isSystem"] == true
            && versions(db, id, 0, 1)?["items"]
                .as_array()
                .is_some_and(Vec::is_empty)
        {
            publish(
                db,
                id,
                body,
                0,
                body["createdAt"]
                    .as_str()
                    .ok_or_else(|| Error::unavailable("内置方案缺少创建时间。"))?,
            )?;
        }
        return Ok(());
    }
    let principal = match kind {
        "users" => "Account",
        "unit-access" => "Unit",
        _ => return Ok(()),
    };
    let mut s = Statement::new(db);
    let kind = s.text(principal);
    let target = s.number(id);
    if !body["permissionGrants"].is_null() || body["permissionTemplateId"].is_null() {
        db.execute_statement(&format!("DELETE FROM permission_bindings WHERE principal_kind={kind} AND principal_id={target}"),&s.values)?;
        return Ok(());
    }
    let scheme = body["permissionTemplateId"]
        .as_i64()
        .ok_or_else(|| Error::unavailable("身份权限方案引用缺失。"))?;
    let version = body["permissionSchemeVersion"]
        .as_i64()
        .filter(|v| *v > 0)
        .ok_or_else(|| Error::new(ErrorKind::Conflict, "请选择权限方案已发布的明确版本。"))?;
    if publication(db, scheme, version)?.is_none() {
        return Err(Error::new(
            ErrorKind::Conflict,
            "所选权限方案版本尚未发布。",
        ));
    }
    let scheme = s.number(scheme);
    let version = s.number(version);
    let reason = s.text(body["permissionNote"].as_str().unwrap_or("明确版本分配"));
    db.execute_statement(&format!("INSERT INTO permission_bindings(principal_kind,principal_id,scheme_id,scheme_version,reason) VALUES({kind},{target},{scheme},{version},{reason}) ON CONFLICT(principal_kind,principal_id) DO UPDATE SET scheme_id=excluded.scheme_id,scheme_version=excluded.scheme_version,reason=excluded.reason"),&s.values)?;
    Ok(())
}
