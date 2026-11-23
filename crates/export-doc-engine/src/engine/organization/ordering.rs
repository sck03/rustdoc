//! Reorder a complete sibling set atomically; hierarchy changes remain separate.
use super::*;
use crate::engine::error::{conflict, invalid};
use std::collections::HashMap;

pub(in crate::engine) fn reorder(store: &Store, actor: &Actor, body: &Value) -> Result<Value> {
    store.transaction_as(actor, |tx, actor| {
        let company = text(body, "companyCode");
        super::super::units::demand(actor, &company, "organization.directory")?;
        let parent = if let Some(id) = body["parentId"].as_i64() {
            let parent = store::get(tx, "departments", id)?;
            if parent["companyCode"] != company {
                return Err(invalid("上级部门不属于当前单位。"));
            }
            text(&parent, "code")
        } else {
            String::new()
        };
        let mut siblings: HashMap<_, _> =
            store::matching(tx, "departments", &[("companyCode", company.clone())], &[])?
                .into_iter()
                .filter(|row| text(row, "parentCode") == parent)
                .map(|row| (row["id"].as_i64().unwrap_or(0), row))
                .collect();
        let items = body["items"]
            .as_array()
            .filter(|v| !v.is_empty() && v.len() <= 2000)
            .ok_or_else(|| invalid("请选择完整的同级部门。"))?;
        if items.len() != siblings.len() {
            return Err(conflict("同级部门已变化，请重新加载后排序。"));
        }
        for (index, item) in items.iter().enumerate() {
            let id = item["id"]
                .as_i64()
                .ok_or_else(|| invalid("部门编号无效。"))?;
            let mut row = siblings
                .remove(&id)
                .ok_or_else(|| conflict("部门重复、层级或单位已变化。"))?;
            store::check_version(&row, store::expected(item))?;
            row["expectedVersion"] = row["versionNumber"].clone();
            row["sortOrder"] = json!(index);
            let scope = row.clone();
            store::save_in_scope(
                tx,
                "departments",
                id,
                row,
                None,
                actor,
                "reorder",
                Some(&scope),
            )?;
        }
        Ok(Value::Null)
    })
}
