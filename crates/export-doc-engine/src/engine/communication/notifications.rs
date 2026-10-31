use super::super::{handling, oa::approval::authority};
use super::*;
use export_doc_storage::NotificationScope;
fn resource(kind: &str) -> Option<&'static str> {
    match kind {
        "supply-requests" => Some("office.supplies"),
        "bookings" => Some("office.rooms"),
        _ => super::super::oa::approval::resource(kind),
    }
}
fn viewable(tx: &Connection, actor: &Actor, resource: &str, row: &Value) -> Result<bool> {
    if matches!(resource, "office.supplies" | "office.rooms") {
        handling::resource_access(tx, actor, resource, "view", row, false)
    } else {
        super::super::oa::viewable(tx, actor, resource, row)
    }
}
fn scopes(tx: &Connection, actor: &Actor) -> Result<Vec<NotificationScope>> {
    let mut result: Vec<_> = super::super::oa::KINDS
        .iter()
        .copied()
        .chain(["supply-requests", "bookings"])
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
                approval_principals: vec![],
            }];
            if kind == "oa-expense" {
                scopes.push(NotificationScope {
                    kind: kind.into(),
                    handling_keys: None,
                    approval_principals: vec![],
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
    for (kind, resource, states) in [
        ("oa-general", "office.general", handling::GENERAL_STATES),
        (
            "supply-requests",
            "office.supplies",
            handling::SUPPLY_STATES,
        ),
        ("bookings", "office.rooms", handling::ROOM_STATES),
    ] {
        result.push(NotificationScope {
            kind: kind.into(),
            rank: 3,
            statuses: states.iter().map(|s| (*s).into()).collect(),
            handling_keys: Some(handling::keys(tx, actor, resource)?),
            approval_principals: vec![],
        });
    }
    {
        for scope in &mut result {
            if let Some(resource) = resource(&scope.kind) {
                scope.approval_principals =
                    super::super::oa::approval::queue_approvers(tx, actor, resource)?;
            }
        }
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
fn project(tx: &Connection, actor: &Actor, mut notification: Value) -> Result<Value> {
    let kind = text(&notification, "requestKind");
    let parent = store::get(
        tx,
        &kind,
        records::positive(&notification, "requestId", "申请")?,
    )?;
    let projected =
        super::super::oa::materials::project(tx, actor, &parent, json!({"title":parent["title"]}))?;
    notification["title"] = projected
        .get("title")
        .filter(|v| v.is_string())
        .cloned()
        .unwrap_or_else(|| json!(format!("申请 #{}", parent["id"])));
    Ok(contracts::dto(
        contracts::schema("SiteNotification"),
        notification,
    ))
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
    project(tx, actor, row)
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
        departments: &actor.departments,
        assigned_only: actor.oa_assigned_only,
        context: &actor.context,
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
            .map(|r| project(tx, actor, r))
            .collect::<Result<Vec<_>>>()?,
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
            | "approval-reassign"
            | "remind"
            | "reject"
            | "complete"
            | "void"
            | "reassign"
            | "issue"
            | "return"
            | "cancel"
    ) {
        return Ok(());
    }
    let mut offset = 0;
    let resource_request = matches!(row["kind"].as_str(), Some("supply" | "room"));
    let assigned_request = resource_request || row["kind"] == "general";
    let completion = action == "approve" && row["status"] == "Approved" && !assigned_request;
    let handling = matches!(action.as_str(), "approve" | "reassign")
        && handling::outstanding(row)
        && assigned_request;
    let review = matches!(
        action.as_str(),
        "submit" | "approve-step" | "remind" | "approval-reassign"
    );
    let cc = if action == "approve" && row["status"] != "Pending" {
        super::super::oa::approval::plan(row)?
            .map(|p| p.notify_user_ids)
            .unwrap_or_default()
    } else {
        vec![]
    };
    let distribution = review || completion || handling || !cc.is_empty();
    let resource = text(meta, "resource");
    loop {
        crate::operation::check()?;
        let users = if distribution {
            tx.query_generic(&export_doc_storage::GenericQuery {
                kind: "users",
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                filters: &[("isActive", "true".into())],
                keyword: "",
                search_root: None,
                fields: &[("id", "id"), ("isActive", "isActive")],
                offset,
                limit: 100,
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
            if !distribution && row["ownerUserId"] != id {
                continue;
            }
            let recipients = match super::super::units::actors_for_operation(
                tx,
                id,
                actor.edition,
                &actor.company,
                &resource,
                if review { "approve" } else { "view" },
            ) {
                Ok(recipients) => recipients,
                Err(e) if matches!(e.status, Some(400 | 401 | 403 | 404)) => continue,
                Err(e) => return Err(e),
            };
            for recipient in recipients {
                if auth::authorize(&recipient, "office.notifications", "view").is_err()
                    || !viewable(tx, &recipient, &resource, row)?
                {
                    continue;
                }
                if completion
                    && !cc.contains(&id)
                    && row["ownerUserId"] != id
                    && !auth::visible(&recipient, &resource, "complete", row)
                {
                    continue;
                }
                if review {
                    let can_review = authority(tx, &recipient, row)?.is_some();
                    if !can_review {
                        continue;
                    }
                }
                if handling
                    && !cc.contains(&id)
                    && row["ownerUserId"] != id
                    && !handling::can_handle(tx, &recipient, row, &resource)?
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
                    json!({"identity":identity,"eventId":event["id"],"requestId":row["id"],"requestKind":match row["kind"].as_str() { Some("supply") => "supply-requests".into(), Some("room") => "bookings".into(), _ => format!("oa-{}",text(row,"kind")) },"title":format!("申请 #{}",row["id"]),"action":action,"status":"Unread","readAt":""}),
                    Some(identity),
                    actor,
                    "notify",
                    Some(&scope),
                )?;
                break;
            }
        }
        if !distribution {
            break;
        }
    }
    Ok(())
}
