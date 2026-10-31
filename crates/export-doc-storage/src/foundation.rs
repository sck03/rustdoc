//! Canonical personnel foundation repositories; callers own the transaction.
mod candidates;
mod queries;
mod writes;
use crate::{Adapter, Error, ErrorKind, Result, sql};
pub(crate) use candidates::list as candidates;
pub(crate) use queries::{appointment, get, list, resolve_duty};
use serde_json::Value;
pub(crate) use writes::{save, sync_person};
pub(crate) fn table(kind: &str) -> Result<&'static str> {
    match kind {
        "positions" => Ok("positions"),
        "employments" => Ok("employments"),
        "responsibilities" => Ok("responsibilities"),
        "projects" => Ok("project_scopes"),
        _ => Err(Error::unavailable("未知人员基础实体。")),
    }
}
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
    fn nullable_number(&mut self, value: Option<&str>) -> String {
        format!(
            "CAST(NULLIF({},'') AS BIGINT)",
            self.text(value.unwrap_or(""))
        )
    }
    fn nullable_text(&mut self, value: Option<&str>) -> String {
        format!("NULLIF({},'')", self.text(value.unwrap_or("")))
    }
    fn json(&mut self, value: &Value) -> Result<String> {
        let parameter = self.text(serde_json::to_string(value)?);
        Ok(if self.pg {
            format!("CAST({parameter} AS JSONB)")
        } else {
            parameter
        })
    }
    fn execute(self, db: &(impl Adapter + ?Sized), query: String) -> Result<u64> {
        db.execute_statement(&query, &self.values)
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
fn json_value(value: &str, pg: bool) -> String {
    if pg {
        value.into()
    } else {
        format!("json({value})")
    }
}
fn boolean(value: &str, pg: bool) -> String {
    if pg {
        format!("({value}=1)")
    } else {
        format!("json(CASE WHEN {value}=1 THEN 'true' ELSE 'false' END)")
    }
}
fn aggregate(select: &str, pg: bool) -> String {
    if pg {
        format!("(SELECT COALESCE(jsonb_agg(body),'[]'::jsonb) FROM ({select}) items)")
    } else {
        format!("(SELECT json_group_array(json(body)) FROM ({select}) items)")
    }
}
fn read_json(select: String, pg: bool) -> String {
    if pg {
        format!("SELECT value::text FROM ({select}) result(value)")
    } else {
        select
    }
}
fn conflict(message: &str) -> Error {
    Error::new(ErrorKind::Conflict, message)
}
