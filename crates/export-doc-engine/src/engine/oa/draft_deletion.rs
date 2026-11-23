//! Delete only never-submitted drafts, retaining audit and retry receipts.
use super::*;

pub(super) fn delete(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    id: i64,
    command: &str,
    body: &Value,
) -> Result<Value> {
    let mut receipt = json!({"id":id});
    if super::super::command_receipts::prepare(tx, actor, &mut receipt, command, body)? {
        return Ok(Value::Null);
    }
    let (_, row) = current(tx, actor, meta, id)?;
    if row["ownerUserId"] != actor.id || text(&row, "_requesterContext") != actor.context {
        return Err(error(403, "只有原申请身份可以删除自己的草稿。"));
    }
    if row["status"] != "Draft"
        || row["submissionId"].as_i64().unwrap_or(0) > 0
        || row["submissionNumber"].as_i64().unwrap_or(0) > 0
        || approval::plan(&row)?.is_some()
        || tx.has_history_except(
            &kind(meta),
            id,
            &["create", "update", "upload", "delete-attachment"],
        )?
    {
        return Err(conflict(
            "只能删除从未提交的草稿；已有审批历史的申请请保留并取消。",
        ));
    }
    store::check_version(&row, store::expected(body))?;
    materials::authorize_edit(tx, actor, &row)?;
    required(body, "note", "删除原因", 500)?;
    for child_kind in ["oa-attachment", "oa-event"] {
        loop {
            let (_, rows) = children(tx, &row, child_kind, 0, 100)?;
            if rows.is_empty() {
                break;
            }
            for child in rows {
                if child["retained"] == true {
                    return Err(conflict("草稿包含正式历史附件，不能删除。"));
                }
                let child_id = records::positive(&child, "id", "关联记录")?;
                tx.delete_blobs(child_id)?;
                if !tx.delete(
                    child_kind,
                    child_id,
                    child["versionNumber"].as_i64().unwrap_or(0),
                )? {
                    return Err(conflict("关联记录已变化，请重新加载。"));
                }
            }
        }
    }
    tx.append_audit_details(
        &export_doc_storage::AuditWrite {
            kind: &kind(meta),
            record_id: id,
            version: row["versionNumber"].as_i64().unwrap_or(0),
            action: "delete-draft",
            actor_id: actor.id,
            occurred_at: &store::timestamp(),
            note: &text(body, "note"),
        },
        &super::super::audit_values::changes(Some(&row), None),
    )?;
    if !tx.delete(&kind(meta), id, row["versionNumber"].as_i64().unwrap_or(0))? {
        return Err(conflict("草稿已被修改，请重新加载。"));
    }
    tx.put_command_receipt(
        &receipt[super::super::command_receipts::FIELD],
        &Value::Null,
    )?;
    Ok(Value::Null)
}
