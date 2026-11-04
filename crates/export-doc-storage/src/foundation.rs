//! Canonical personnel foundation repositories; callers own the transaction.
pub(crate) mod authority;
mod candidates;
mod projects;
mod queries;
mod writes;
use crate::{Adapter, Error, ErrorKind, Result, sql};
pub(crate) use candidates::list as candidates;
pub(crate) use projects::options as project_options;
pub(crate) use queries::{appointment, catalog, get, list, placement_appointments, resolve_duty};
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
use crate::sql::{Statement, object};
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
