//! JSON is confined to the record boundary; item identity and draft rules are typed.
use super::*;
use crate::generated_api::ApiInvoiceDetailDto;
use serde::Deserialize;
use std::collections::BTreeSet;

#[derive(Default, Deserialize)]
#[serde(default)]
struct SavedItems {
    status: String,
    items: Vec<ItemIdentity>,
    #[serde(rename = "_nextItemId")]
    next: Option<i64>,
}
#[derive(Deserialize)]
struct ItemIdentity {
    id: i64,
}

pub(super) fn normalize(id: i64, previous: &Value, value: &mut Value) -> Result<()> {
    let saved = if id > 0 {
        SavedItems::deserialize(previous)?
    } else {
        SavedItems::default()
    };
    if id > 0 && saved.status != "Draft" {
        return Err(conflict(
            "已核对、出运、结汇或作废的发票不能编辑，请先按业务流程撤销核对。",
        ));
    }
    let dto = ApiInvoiceDetailDto::deserialize(&*value)
        .map_err(|cause| invalid(format!("发票字段无效：{cause}")))?;
    let (dto, next) = prepare(id, dto, saved)?;
    value["_nextItemId"] = json!(next);
    *value = contracts::overlay(value.clone(), &serde_json::to_value(dto)?);
    Ok(())
}
fn prepare(
    id: i64,
    dto: ApiInvoiceDetailDto,
    saved: SavedItems,
) -> Result<(ApiInvoiceDetailDto, i64)> {
    let mut dto = crate::invoice::InvoiceDraft::from_dto(dto)
        .build()
        .map_err(invalid)?;
    dto.status = "Draft".into();
    let existing: BTreeSet<_> = saved.items.into_iter().map(|item| item.id).collect();
    let successor = |id: i64| {
        id.checked_add(1)
            .ok_or_else(|| unavailable("发票货项编号超出范围。"))
    };
    let mut next = saved
        .next
        .unwrap_or(1)
        .max(successor(existing.last().copied().unwrap_or(0))?);
    let mut assigned = BTreeSet::new();
    for item in &mut dto.items {
        if id == 0 || item.id == 0 {
            item.id = next;
            next = successor(next)?;
        } else if !existing.contains(&item.id) {
            return Err(invalid("货项编号不属于当前发票；新货项编号须为 0。"));
        }
        if !assigned.insert(item.id) {
            return Err(invalid("发票货项编号不能重复。"));
        }
        item.invoice_id = id;
    }
    Ok((dto, next))
}
