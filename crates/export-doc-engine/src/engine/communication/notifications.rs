use super::super::oa::{approval::authority, handling};
use super::*;
use export_doc_storage::NotificationScope;
fn resource(kind: &str) -> Option<&'static str> {
    if kind == "supply-requests" {
        Some("office.supplies")
    } else {
        super::super::oa::approval::resource(kind)
    }
}
fn viewable(tx: &Connection, actor: &Actor, resource: &str, row: &Value) -> Result<bool> {
    if resource == "office.supplies" {
        handling::supply_access(tx, actor, "view", row, false)
    } else {
        super::super::oa::viewable(tx, actor, resource, row)
    }
}
fn scopes(tx: &Connection, actor: &Actor) -> Result<Vec<NotificationScope>> {
    let mut result: Vec<_> = super::super::oa::KINDS
        .iter()
        .copied()
        .chain(std::iter::once("supply-requests"))
        .flat_map(|kind| {
            let Some(resource) = resource(kind) else {
                return vec![];
            };
            if auth::authorize(actor, resource, "view").is_err() {
                return vec![];
            }
            let mut scopes = vec![NotificationScope {
                kind: kind.into(),
                rank: auth::scope_rank(actor, resource, "view"),
                statuses: vec![],
                handling_keys: None,
            }];
            if kind == "oa-expense" {
                scopes.push(NotificationScope {
                    kind: kind.into(),
                    handling_keys: None,
                    rank: auth::scope_rank(actor, resource, "complete"),
                    statuses: super::super::oa::FINANCE_STATUSES
                        .iter()
                        .map(|s| (*s).into())
                        .collect(),
                });
            }
            scopes
        })
        .collect();
    for (kind, supply, states) in [
        ("oa-general", false, handling::GENERAL_STATES),
        ("supply-requests", true, handling::SUPPLY_STATES),
    ] {
        result.push(NotificationScope {
            kind: kind.into(),
            rank: 3,
            statuses: states.iter().map(|s| (*s).into()).collect(),
            handling_keys: Some(handling::keys(tx, actor, supply)?),
        });
    }
    Ok(result)
}
fn accessible(tx: &Connection, actor: &Actor, row: &Value) -> Result<()> {
    if row["companyScope"] != actor.company || row["ownerUserId"] != actor.id {
        return Err(error(403, "只能访问自己的站内通知。"));
    }
    let kind = text(row, "requestKind");
    let resource =
        resource(&kind).ok_or_else(|| super::super::error::unavailable("通知关联类型无效。"))?;
    let parent = store::get(tx, &kind, records::positive(row, "requestId", "申请")?)?;
    if !viewable(tx, actor, resource, &parent)? {
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
    let scopes = scopes(tx, actor)?;
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
        "submit"
            | "approve"
            | "approve-step"
            | "remind"
            | "reject"
            | "complete"
            | "void"
            | "reassign"
    ) {
        return Ok(());
    }
    let mut offset = 0;
    let completion = action == "approve"
        && row["status"] == "Approved"
        && !matches!(row["kind"].as_str(), Some("general" | "supply"));
    let handling = matches!(action.as_str(), "approve" | "reassign")
        && (row["status"] == "Approved" || action == "reassign" && row["status"] == "Issued")
        && matches!(row["kind"].as_str(), Some("general" | "supply"));
    let review = matches!(action.as_str(), "submit" | "approve-step" | "remind");
    let distribution = review || completion || handling;
    let resource = text(meta, "resource");
    loop {
        crate::operation::check()?;
        let users = if distribution {
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
            if (review && id == actor.id) || (!distribution && row["ownerUserId"] != id) {
                continue;
            }
            let recipient = auth::current_actor_in(tx, id, actor.edition)?;
            if auth::authorize(&recipient, "office.notifications", "view").is_err()
                || !viewable(tx, &recipient, &resource, row)?
            {
                continue;
            }
            if completion
                && row["ownerUserId"] != id
                && !auth::visible(&recipient, &resource, "complete", row)
            {
                continue;
            }
            if review && authority(tx, &recipient, row)?.is_none() {
                continue;
            }
            if handling
                && row["ownerUserId"] != id
                && !handling::can_handle(tx, &recipient, row, row["kind"] == "supply")?
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
                json!({"identity":identity,"eventId":event["id"],"requestId":row["id"],"requestKind":if row["kind"] == "supply" {"supply-requests".into()} else {format!("oa-{}",text(row,"kind"))},"title":row["title"],"action":action,"status":"Unread","readAt":""}),
                Some(identity),
                actor,
                "notify",
                Some(&scope),
            )?;
        }
        if !distribution {
            break;
        }
    }
    Ok(())
}
