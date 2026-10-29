//! CRM lists and overview share scoped, bounded reads without loading unrelated record bodies.
use super::{
    auth,
    error::{Result, error},
    store::Actor,
};
use crate::contracts;
use crate::engine::store::Connection;
use export_doc_storage::{CrmList, CrmQuery};
use serde_json::Value;

pub(super) fn fields(schema: &str) -> Vec<(&'static str, &'static str)> {
    contracts::schema(schema)["properties"]
        .as_object()
        .into_iter()
        .flat_map(|properties| properties.keys())
        .map(|key| (key.as_str(), key.as_str()))
        .collect()
}
fn request<'a>(
    actor: &'a Actor,
    list: CrmList<'a>,
    fields: &'a [(&'a str, &'a str)],
    offset: i64,
    limit: i64,
) -> CrmQuery<'a> {
    let permission = match list {
        CrmList::Contacts => "sales.contacts",
        CrmList::FollowUps { .. } => "sales.follow-ups",
        CrmList::Opportunities { .. } => "sales.opportunities",
    };
    CrmQuery {
        list,
        scope: auth::data_scope(actor, permission, "view"),
        customer_scope: auth::data_scope(actor, "sales.customers", "view"),
        fields,
        offset,
        limit,
    }
}
pub(super) fn page(
    tx: &Connection,
    actor: &Actor,
    list: CrmList<'_>,
    fields: &[(&str, &str)],
    offset: i64,
    limit: i64,
) -> Result<(usize, Vec<Value>)> {
    let mut fields = fields.to_vec();
    if fields.is_empty() {
        fields.push(("id", "id"));
    }
    let opportunity = matches!(list, CrmList::Opportunities { .. });
    let include_product_id = fields.iter().any(|(name, _)| *name == "productId");
    if opportunity {
        if !include_product_id {
            fields.push(("productId", "productId"));
        }
        fields.push(("__linkedProductId", "linkedProductId"));
    }
    let (total, mut rows) = tx.query_crm(&request(actor, list, &fields, offset, limit))?;
    if opportunity {
        for row in &mut rows {
            if row["productId"].as_i64().is_some_and(|id| id > 0)
                && row["__linkedProductId"].as_i64().is_none()
            {
                return Err(error(404, "关联商品不存在。"));
            }
            if let Some(object) = row.as_object_mut() {
                object.remove("__linkedProductId");
                if !include_product_id {
                    object.remove("productId");
                }
            }
        }
    }
    Ok((total as usize, rows))
}
pub(super) fn scan(
    tx: &Connection,
    actor: &Actor,
    list: CrmList<'_>,
    fields: &[(&str, &str)],
    mut visit: impl FnMut(Value) -> Result<()>,
) -> Result<()> {
    let mut fields = fields.to_vec();
    if !fields.iter().any(|(name, _)| *name == "id") {
        fields.push(("id", "id"));
    }
    let mut after_id = 0;
    loop {
        crate::operation::check()?;
        let rows = tx.scan_crm(&request(actor, list, &fields, 0, 200), after_id)?;
        let count = rows.len();
        for row in rows {
            let id = row["id"]
                .as_i64()
                .filter(|id| *id > after_id)
                .ok_or_else(|| super::error::unavailable("CRM 记录编号损坏。"))?;
            after_id = id;
            visit(row)?;
        }
        if count < 200 {
            return Ok(());
        }
    }
}
