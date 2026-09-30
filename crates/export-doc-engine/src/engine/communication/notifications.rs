use super::super::oa::approval::{authority, resource};
use super::*;
use export_doc_storage::NotificationScope;
fn scopes(actor: &Actor) -> Vec<NotificationScope> {
    super::super::oa::KINDS
        .iter()
        .flat_map(|kind| {
            let Some(resource) = resource(kind) else {
                return vec![];
            };
            if auth::authorize(actor, resource, "view").is_err() {
                return vec![];
            }
            let mut scopes = vec![NotificationScope {
                kind: (*kind).into(),
                rank: auth::scope_rank(actor, resource, "view"),
                statuses: vec![],
            }];
            if *kind == "oa-expense" {
                scopes.push(NotificationScope {
                    kind: (*kind).into(),
                    rank: auth::scope_rank(actor, resource, "complete"),
                    statuses: super::super::oa::FINANCE_STATUSES
                        .iter()
                        .map(|s| (*s).into())
                        .collect(),
                });
            }
            scopes
        })
        .collect()
}
fn accessible(tx: &Connection, actor: &Actor, row: &Value) -> Result<()> {
    if row["companyScope"] != actor.company || row["ownerUserId"] != actor.id {
        return Err(error(403, "只能访问自己的站内通知。"));
    }
    let kind = text(row, "requestKind");
    let resource =
        resource(&kind).ok_or_else(|| super::super::error::unavailable("通知关联类型无效。"))?;
    let parent = store::get(tx, &kind, records::positive(row, "requestId", "申请")?)?;
    if !super::super::oa::viewable(actor, resource, &parent) {
        return Err(error(403, "您已无权访问此通知关联的申请。"));
    }
    Ok(())
}
fn mark(tx: &Connection, actor: &Actor, mut row: Value) -> Result<Value> {
    accessible(tx, actor, &row)?;
    if row["status"] == "Unread" {
        row["status"] = json!("Read");
        row["readAt"] = json!(store::timestamp());
        let id = records::positive(&row, "id", "通知")?;
        let identity = text(&row, "identity");
        row = store::save(
            tx,
            "site-notification",
            id,
            row,
            Some(identity),
            actor,
            "read",
        )?;
    }
    Ok(contracts::dto(contracts::schema("SiteNotification"), row))
}
pub(super) fn handle(
    tx: &Connection,
    actor: &Actor,
    meta: &Value,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
) -> Result<Value> {
    let action = text(meta, "action");
    if action == "notification-read" {
        return mark(
            tx,
            actor,
            store::get(tx, "site-notification", records::id(parameters)?)?,
        );
    }
    let scopes = scopes(actor);
    let (number, size) = paging(query)?;
    let mut q = CommunicationQuery {
        company: &actor.company,
        department: &actor.department,
        reader: actor.id,
        unread_only: action != "inbox" || unread(query)?,
        view: CommunicationView::Notifications { scopes: &scopes },
        offset: (number - 1) * size,
        limit: size,
    };
    if action == "read-all" {
        q.offset = 0;
        q.limit = 100;
        loop {
            crate::operation::check()?;
            let (_, rows) = tx.query_communications(&q)?;
            if rows.is_empty() {
                break;
            }
            for row in rows {
                mark(tx, actor, row)?;
            }
        }
        return Ok(json!({"unreadCount":0}));
    }
    let (count, rows) = tx.query_communications(&q)?;
    if action == "unread-count" {
        return Ok(json!({"unreadCount":count}));
    }
    Ok(page(
        rows.into_iter()
            .map(|r| contracts::dto(contracts::schema("SiteNotification"), r))
            .collect(),
        count,
        number,
        size,
    ))
}
pub(in crate::engine) fn on_event(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    event: &Value,
    meta: &Value,
) -> Result<()> {
    let action = text(event, "action");
    if !matches!(
        action.as_str(),
        "submit" | "approve" | "approve-step" | "remind" | "reject" | "complete" | "void"
    ) {
        return Ok(());
    }
    let mut offset = 0;
    let finance = action == "approve" && row["status"] == "Approved" && row["kind"] == "expense";
    let review = matches!(action.as_str(), "submit" | "approve-step" | "remind");
    let resource = text(meta, "resource");
    loop {
        crate::operation::check()?;
        let users = if review || finance {
            tx.query_records(&RecordQuery {
                kind: "users",
                company: &actor.company,
                offset,
                limit: 100,
                ..Default::default()
            })?
            .1
        } else {
            vec![store::get(
                tx,
                "users",
                records::positive(row, "ownerUserId", "申请人")?,
            )?]
        };
        if users.is_empty() {
            break;
        }
        offset += users.len() as i64;
        for user in users {
            if user["isActive"] != true {
                continue;
            }
            let id = records::positive(&user, "id", "接收人")?;
            if (review && id == actor.id) || (!review && !finance && row["ownerUserId"] != id) {
                continue;
            }
            let recipient = auth::current_actor_in(tx, id, actor.edition)?;
            if auth::authorize(&recipient, "office.notifications", "view").is_err()
                || !super::super::oa::viewable(&recipient, &resource, row)
            {
                continue;
            }
            if finance
                && row["ownerUserId"] != id
                && !auth::visible(&recipient, &resource, "complete", row)
            {
                continue;
            }
            if review && authority(tx, &recipient, row)?.is_none() {
                continue;
            }
            let identity = format!("{}:{id}", event["id"]);
            if tx.find_identity("site-notification", &identity)?.is_some() {
                continue;
            }
            let scope = json!({"companyScope":recipient.company,"departmentId":recipient.department,"ownerUserId":id});
            store::save_in_scope(
                tx,
                "site-notification",
                0,
                json!({"identity":identity,"eventId":event["id"],"requestId":row["id"],"requestKind":format!("oa-{}",text(row,"kind")),"title":row["title"],"action":action,"status":"Unread","readAt":""}),
                Some(identity),
                actor,
                "notify",
                Some(&scope),
            )?;
        }
        if !review && !finance {
            break;
        }
    }
    Ok(())
}
