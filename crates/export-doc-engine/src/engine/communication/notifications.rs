use super::*;
use export_doc_storage::NotificationScope;

fn resource(kind: &str) -> Option<&'static str> {
    match kind {
        "oa-leave" => Some("office.leave"),
        "oa-overtime" => Some("office.overtime"),
        "oa-expense" => Some("office.expenses"),
        "oa-travel" => Some("office.travel"),
        "oa-purchase" => Some("office.purchase"),
        "oa-general" => Some("office.general"),
        _ => None,
    }
}
fn scopes(actor: &Actor) -> Vec<NotificationScope> {
    super::super::oa::KINDS
        .iter()
        .filter_map(|kind| {
            let resource = resource(kind)?;
            if auth::authorize(actor, resource, "view").is_err() {
                return None;
            }
            let rank = if actor.admin {
                4
            } else {
                actor
                    .grants
                    .iter()
                    .filter(|g| g["resourceKey"] == resource && g["action"] == "view")
                    .map(|g| {
                        export_doc_domain::permissions::scope_rank(
                            g["dataScope"].as_str().unwrap_or(""),
                        )
                    })
                    .max()
                    .unwrap_or(0)
            };
            Some(NotificationScope {
                kind: (*kind).into(),
                rank,
            })
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
    auth::authorize(actor, resource, "view")?;
    if parent["companyScope"] != actor.company || !auth::visible(actor, resource, "view", &parent) {
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
        "submit" | "approve" | "reject" | "complete" | "void"
    ) {
        return Ok(());
    }
    let mut offset = 0;
    loop {
        crate::operation::check()?;
        let (_, users) = tx.query_records(&RecordQuery {
            kind: "users",
            company: &actor.company,
            offset,
            limit: 100,
            ..Default::default()
        })?;
        if users.is_empty() {
            break;
        }
        offset += users.len() as i64;
        for user in users {
            if user["isActive"] != true {
                continue;
            }
            let id = records::positive(&user, "id", "接收人")?;
            if (action == "submit" && id == actor.id)
                || (action != "submit" && row["ownerUserId"] != id)
            {
                continue;
            }
            let recipient = auth::current_actor_in(tx, id, actor.edition)?;
            let resource = text(meta, "resource");
            if auth::authorize(&recipient, "office.notifications", "view").is_err()
                || auth::authorize(&recipient, &resource, "view").is_err()
                || !auth::visible(&recipient, &resource, "view", row)
            {
                continue;
            }
            if action == "submit"
                && (auth::authorize(&recipient, &resource, "approve").is_err()
                    || !auth::visible(&recipient, &resource, "approve", row))
            {
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
    }
    Ok(())
}
