use super::*;
const RESOURCE: &str = "office.announcements";

fn readable(actor: &Actor, row: &Value) -> Result<()> {
    auth::authorize(actor, RESOURCE, "view")?;
    let now = store::timestamp();
    if row["companyScope"] != actor.company
        || row["status"] != "Published"
        || text(row, "startsAt") > now
        || text(row, "expiresAt") <= now
        || (!text(row, "audienceDepartment").is_empty()
            && row["audienceDepartment"] != actor.department)
    {
        return Err(error(403, "此公告当前不在您的可见范围或有效期内。"));
    }
    Ok(())
}
pub(in crate::engine) fn current(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    id: i64,
) -> Result<(Actor, Value)> {
    let actor = auth::current_actor_in(tx, actor.id, actor.edition)?;
    auth::authorize(&actor, RESOURCE, &text(meta, "permission"))?;
    let row = store::get(tx, "announcement", id)?;
    if row["companyScope"] != actor.company {
        return Err(error(403, "不能访问其它公司的公告。"));
    }
    if meta["permission"] == "view"
        && (meta["action"] == "read" || auth::authorize(&actor, RESOURCE, "manage").is_err())
    {
        readable(&actor, &row)?;
    }
    Ok((actor, row))
}
pub(in crate::engine) fn mutable(row: &Value) -> Result<()> {
    if !matches!(row["status"].as_str(), Some("Draft" | "Withdrawn")) {
        return Err(conflict("请先撤回公告再修改；已归档公告不可修改。"));
    }
    Ok(())
}
fn fields(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    required(body, "requestKey", "请求编号", 100)?;
    required(body, "title", "公告标题", 200)?;
    required(body, "body", "公告正文", 20000)?;
    let (start, end) = export_doc_domain::announcement::validity(
        &text(body, "startsAt"),
        &text(body, "expiresAt"),
    )
    .map_err(invalid)?;
    let department = text(body, "audienceDepartment");
    super::super::organization::validate_assignment(tx, &actor.company, &department)?;
    let mut value = contracts::dto(contracts::schema("AnnouncementSave"), body.clone());
    value.as_object_mut().unwrap().remove("expectedVersion");
    value["startsAt"] = json!(start);
    value["expiresAt"] = json!(end);
    value["title"] = json!(text(body, "title"));
    value["body"] = json!(text(body, "body"));
    Ok(value)
}
fn receipt_identity(row: &Value, reader: i64) -> String {
    format!("{}:{}:{reader}", row["id"], row["publishVersion"])
}
pub(in crate::engine) fn project(
    tx: &Connection,
    actor: &Actor,
    mut row: Value,
    detail: bool,
) -> Result<Value> {
    row["attachments"] = if detail {
        json!(children(tx, &row, "announcement-attachment", 0, 20)?.1)
    } else {
        json!([])
    };
    row["readAt"] = tx
        .find_identity("announcement-receipt", &receipt_identity(&row, actor.id))?
        .map(|v| v["readAt"].clone())
        .unwrap_or(json!(""));
    Ok(contracts::dto(contracts::schema("Announcement"), row))
}
pub(in crate::engine) fn save(
    tx: &Connection,
    actor: &Actor,
    mut row: Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    let id = row["id"].as_i64().unwrap_or(0);
    let identity = text(&row, "identity");
    row["lastActionNote"] = json!(note);
    let saved = store::save(tx, "announcement", id, row, Some(identity), actor, action)?;
    // Publish snapshots preserve the exact text and audience acknowledged by each receipt.
    if action == "publish" {
        let mut snapshot = saved.clone();
        snapshot.as_object_mut().unwrap().remove("id");
        snapshot["requestId"] = saved["id"].clone();
        snapshot["note"] = json!(note);
        store::save_in_scope(
            tx,
            "announcement-publication",
            0,
            snapshot,
            Some(format!("{}:{}", saved["id"], saved["publishVersion"])),
            actor,
            action,
            Some(&saved),
        )?;
    }
    project(tx, actor, saved, true)
}
pub(super) fn handle(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let action = text(meta, "action");
    if action == "departments" {
        return Ok(json!(
            store::all(tx, "departments")?
                .into_iter()
                .filter(|r| r["companyCode"] == actor.company && r["isActive"] == true)
                .map(|r| json!({"code":r["code"],"name":r["name"]}))
                .collect::<Vec<_>>()
        ));
    }
    if matches!(action.as_str(), "list" | "manage-list") {
        let (number, size) = paging(query)?;
        let now = store::timestamp();
        let (count, rows) = tx.query_communications(&CommunicationQuery {
            company: &actor.company,
            department: &actor.department,
            reader: actor.id,
            unread_only: unread(query)?,
            view: CommunicationView::Announcements {
                manage: action == "manage-list",
                now: &now,
            },
            offset: (number - 1) * size,
            limit: size,
        })?;
        return Ok(page(
            rows.into_iter()
                .map(|r| project(tx, actor, r, false))
                .collect::<Result<Vec<_>>>()?,
            count,
            number,
            size,
        ));
    }
    if action == "create" {
        let mut row = fields(tx, actor, body)?;
        let identity = store::normalize(&serde_json::to_string(&json!([
            actor.company,
            actor.id,
            row["requestKey"]
        ]))?);
        let digest = super::super::media::digest(&serde_json::to_vec(&row)?);
        if let Some(existing) = tx.find_identity("announcement", &identity)? {
            if existing["submissionDigest"] != digest {
                return Err(conflict("同一请求编号不能用于不同公告内容。"));
            }
            return project(tx, actor, existing, true);
        }
        row["identity"] = json!(identity);
        row["submissionDigest"] = json!(digest);
        row["status"] = json!("Draft");
        row["publishVersion"] = json!(0);
        row["authorName"] = json!(actor.name);
        row["publishedAt"] = json!("");
        return save(tx, actor, row, "create", "");
    }
    let (_, mut row) = current(tx, actor, meta, records::id(parameters)?)?;
    if action == "get" {
        return project(tx, actor, row, true);
    }
    if action == "receipts" {
        let (number, size) = paging(query)?;
        let (count, items) = children(tx, &row, "announcement-receipt", (number - 1) * size, size)?;
        return Ok(contracts::dto(
            contracts::schema("AnnouncementReceiptPage"),
            page(items, count, number, size),
        ));
    }
    if action == "read" {
        readable(actor, &row)?;
        if body["publishVersion"] != row["publishVersion"] {
            return Err(conflict("公告已重新发布，请阅读最新内容后再确认。"));
        }
        let identity = receipt_identity(&row, actor.id);
        if tx
            .find_identity("announcement-receipt", &identity)?
            .is_none()
        {
            store::save(
                tx,
                "announcement-receipt",
                0,
                json!({"requestId":row["id"],"publishVersion":row["publishVersion"],"readerName":actor.name,"readAt":store::timestamp()}),
                Some(identity),
                actor,
                "read",
            )?;
        }
        return project(tx, actor, row, true);
    }
    store::check_version(&row, store::expected(body))?;
    if action == "delete" {
        if row["publishVersion"] != 0
            || !matches!(row["status"].as_str(), Some("Draft" | "Archived"))
        {
            return Err(conflict(
                "发布过的公告不可删除，请归档以保留发布历史和阅读回执。",
            ));
        }
        required(body, "note", "删除原因", 500)?;
        let id = records::positive(&row, "id", "公告")?;
        for child in ["announcement-publication", "announcement-receipt"] {
            if children(tx, &row, child, 0, 1)?.0 > 0 {
                return Err(conflict("公告已有发布历史或回执，不能删除。"));
            }
        }
        for attachment in children(tx, &row, "announcement-attachment", 0, 20)?.1 {
            crate::operation::check()?;
            let file = records::positive(&attachment, "id", "附件")?;
            tx.delete_blobs(file)?;
            if !tx.delete(
                "announcement-attachment",
                file,
                store::expected(&attachment),
            )? {
                return Err(conflict("附件已变化，删除已取消。"));
            }
        }
        tx.append_audit_details(
            &export_doc_storage::AuditWrite {
                kind: "announcement",
                record_id: id,
                version: store::expected(&row),
                action: "delete",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: &text(body, "note"),
            },
            &super::super::audit_values::changes(Some(&row), None),
        )?;
        if !tx.delete("announcement", id, store::expected(&row))? {
            return Err(conflict("公告已变化，删除已取消。"));
        }
        return Ok(json!({"success":true}));
    }
    if action == "delete-attachment" {
        return super::super::record_documents::remove(tx, actor, meta, row, parameters, body);
    }
    if action == "update" {
        mutable(&row)?;
        if row["requestKey"] != body["requestKey"] {
            return Err(invalid("公告请求编号不可更换。"));
        }
        for (k, v) in fields(tx, actor, body)?.as_object().unwrap() {
            row[k] = v.clone();
        }
    } else {
        let next = export_doc_domain::announcement::next_status(&text(&row, "status"), &action)
            .ok_or_else(|| conflict("当前公告状态不能执行此操作。"))?;
        if action == "publish" {
            fields(tx, actor, &row)?;
            if text(&row, "expiresAt") <= store::timestamp() {
                return Err(invalid("不能发布已经过期的公告，请调整有效期。"));
            }
            row["publishVersion"] = json!(row["publishVersion"].as_i64().unwrap_or(0) + 1);
            row["publishedAt"] = json!(store::timestamp());
        } else {
            required(body, "note", "操作原因", 500)?;
        }
        row["status"] = json!(next);
    }
    save(tx, actor, row, &action, &text(body, "note"))
}
