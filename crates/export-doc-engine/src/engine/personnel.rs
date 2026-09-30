use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    media, records,
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*};
use export_doc_storage::BlobWrite;
use serde_json::{Value, json};

pub fn save(
    tx: &export_doc_storage::Connection,
    actor: &Actor,
    id: i64,
    mut value: Value,
    action: &str,
    note: &str,
) -> Result<Value> {
    let event_action = match action {
        "create" => "Hire",
        "edit" => "Edit",
        "image-upload" | "image-delete" => "Image",
        "attachment-upload" | "attachment-delete" => "Edit",
        "account-link" => "LinkAccount",
        "ConfirmPersonnel" => "Confirm",
        "TransferPersonnel" => "Transfer",
        "DepartPersonnel" => "Depart",
        "RehirePersonnel" => "Rehire",
        _ => return Err(invalid("人员变动类型无效。")),
    };
    let audit_action = if matches!(event_action, "Confirm" | "Transfer" | "Depart" | "Rehire") {
        "transition"
    } else {
        action
    };
    if let Some(user_id) = value["account"]["id"].as_i64() {
        let mut user = store::get(tx, "users", user_id)?;
        if user["companyScope"] != value["companyScope"] || user["role"] == "Admin" {
            return Err(unavailable("人员关联账号不符合公司或角色约束。"));
        }
        let departed = value["status"] == "Departed";
        if user["fullName"] != value["profile"]["fullName"]
            || user["departmentId"] != value["departmentId"]
            || (departed && user["isActive"] == true)
        {
            user["fullName"] = value["profile"]["fullName"].clone();
            user["departmentId"] = value["departmentId"].clone();
            if departed {
                user["isActive"] = json!(false);
            }
            let username = records::text(&user, "username");
            user = store::save(
                tx,
                "users",
                user_id,
                user,
                Some(username),
                actor,
                "personnel-sync",
            )?;
        }
        value["account"] = contracts::dto(contracts::schema("PersonnelAccountRecord"), user);
    }
    let identity = records::text(&value, "employeeNumber");
    let saved = store::save(tx, "people", id, value, Some(identity), actor, audit_action)?;
    store::save(
        tx,
        "personnel-events",
        0,
        json!({
            "employeeId":saved["id"],"companyScope":saved["companyScope"],"action":event_action,
            "effectiveDate":saved["lastEffectiveDate"],"actorName":actor.name,
            "summary":format!("{} · {}",records::text(&saved,"departmentId"),records::text(&saved,"jobTitle")),
            "note":note,"createdAt":store::timestamp()
        }),
        None,
        actor,
        event_action,
    )?;
    super::personnel_queries::detail(tx, actor, saved)
}

pub const OPERATIONS: &[Operation] = &[
    GET_PERSONNEL_CLEARANCE,
    LINK_PERSONNEL_ACCOUNT,
    LIST_PERSONNEL_ACCOUNT_OPTIONS,
    UPLOAD_PERSONNEL_IMAGE,
    DELETE_PERSONNEL_IMAGE,
    GET_PERSONNEL_IMAGE,
    GET_PERSONNEL_AVATAR,
];

pub(super) fn person(
    tx: &export_doc_storage::Connection,
    actor: &Actor,
    id: i64,
    action: &str,
) -> Result<Value> {
    auth::authorize(actor, "office.people", action)?;
    let value = store::get(tx, "people", id)?;
    if !auth::visible(actor, "office.people", action, &value) {
        return Err(error(403, "没有访问此人员档案的权限。"));
    }
    Ok(value)
}

fn image_kind<'a>(parameters: &'a [(&str, String)]) -> Result<&'a str> {
    let kind = parameters
        .iter()
        .find(|(key, _)| *key == "kind")
        .map(|(_, value)| value.as_str())
        .unwrap_or("Avatar");
    if !["Avatar", "IdentityFront", "IdentityBack"].contains(&kind) {
        return Err(invalid("人员图片类别无效。"));
    }
    Ok(kind)
}

pub fn upload(
    store: &Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    metadata: Value,
    bytes: &[u8],
) -> Result<Value> {
    let id = records::id(parameters)?;
    let kind = image_kind(parameters)?;
    let content_type = media::image_type(bytes, 5 * 1024 * 1024)?;
    let hash = media::digest(bytes);
    store.transaction_as(actor, |tx, actor| {
        let mut person = person(tx, actor, id, "edit")?;
        if person["status"] == "Departed" { return Err(conflict("离职人员的图片不能修改。")); }
        if person["images"].as_array().is_some_and(|images| images.iter().any(|image| image["kind"] == kind && image["contentHash"] == hash)) {
            return super::personnel_queries::detail(tx, actor, person);
        }
        store::check_version(&person, store::expected(&metadata))?;
        let images = person["images"].as_array_mut().ok_or_else(|| unavailable("人员图片记录损坏。"))?;
        images.retain(|image| image["kind"] != kind);
        images.push(json!({"kind":kind,"contentType":content_type,"byteLength":bytes.len(),"contentHash":hash}));
        if kind == "Avatar" { person["employee"]["avatarHash"] = json!(hash); }
        let file_kind = format!("personnel:{kind}");
        tx.delete_blob(id, &file_kind)?;
        tx.insert_blob(&BlobWrite { kind:&file_kind,record_id:id,file_name:kind,media_type:content_type,digest:&hash,content:bytes,created_at:&store::timestamp() })?;
        save(tx, actor, id, person, "image-upload", "")
    })
}

pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    let id = records::id(parameters)?;
    let action = match operation {
        GET_PERSONNEL_AVATAR => "view",
        GET_PERSONNEL_IMAGE | GET_PERSONNEL_CLEARANCE => "view-details",
        DELETE_PERSONNEL_IMAGE => "edit",
        _ => "assign",
    };
    let current = person(&*store.connection()?, actor, id, action)?;
    let result = match operation {
        GET_PERSONNEL_CLEARANCE => {
            store.transaction(|tx| super::office_queries::clearance(tx, &current))?
        }
        GET_PERSONNEL_AVATAR | GET_PERSONNEL_IMAGE => {
            let kind = if operation == GET_PERSONNEL_AVATAR {
                "Avatar"
            } else {
                image_kind(parameters)?
            };
            let blob = store
                .connection()?
                .blob(id, &format!("personnel:{kind}"))?
                .ok_or_else(|| error(404, "人员图片不存在。"))?;
            if media::digest(&blob.content) != blob.digest {
                return Err(unavailable("人员图片内容校验失败。"));
            }
            return Ok(blob.content);
        }
        DELETE_PERSONNEL_IMAGE => {
            let kind = image_kind(parameters)?;
            let expected = query
                .iter()
                .find(|(key, _)| *key == "expectedVersion")
                .and_then(|(_, value)| value.parse().ok())
                .unwrap_or(0);
            store.transaction_as(actor, |tx, actor| {
                let mut person = person(tx, actor, id, "edit")?;
                store::check_version(&person, expected)?;
                if person["status"] == "Departed" {
                    return Err(conflict("离职档案不能修改。"));
                }
                person["images"]
                    .as_array_mut()
                    .ok_or_else(|| unavailable("图片记录损坏。"))?
                    .retain(|image| image["kind"] != kind);
                if kind == "Avatar" {
                    person["employee"]["avatarHash"] = Value::Null;
                }
                tx.delete_blob(id, &format!("personnel:{kind}"))?;
                save(tx, actor, id, person, "image-delete", "")
            })?
        }
        LIST_PERSONNEL_ACCOUNT_OPTIONS | LINK_PERSONNEL_ACCOUNT => {
            if !actor.admin {
                return Err(error(403, "只有管理员可以关联账号。"));
            }
            if operation == LIST_PERSONNEL_ACCOUNT_OPTIONS {
                let people = store.all("people")?;
                let accounts = store
                    .all("users")?
                    .into_iter()
                    .filter(|user| {
                        user["companyScope"] == current["companyScope"]
                            && user["isActive"] == true
                            && user["role"] != "Admin"
                            && !people
                                .iter()
                                .any(|p| p["id"] != id && p["account"]["id"] == user["id"])
                    })
                    .map(|user| {
                        contracts::overlay(
                            contracts::initial(contracts::schema("PersonnelAccountRecord")),
                            &user,
                        )
                    })
                    .collect();
                store::paged(accounts, query)
            } else {
                store.transaction_as(actor, |tx, actor| {
                    let mut person = person(tx, actor, id, "assign")?;
                    if !actor.admin {
                        return Err(error(403, "只有管理员可以关联账号。"));
                    }
                    store::check_version(&person, store::expected(body))?;
                    if person["status"] == "Departed" {
                        return Err(conflict("离职档案不能关联账号。"));
                    }
                    let user_id = body["userId"]
                        .as_i64()
                        .filter(|id| *id > 0)
                        .ok_or_else(|| invalid("请选择有效账号。"))?;
                    let mut user = store::get(tx, "users", user_id)?;
                    store::check_version(
                        &user,
                        body["expectedAccountVersion"].as_i64().unwrap_or(0),
                    )?;
                    if user["companyScope"] != person["companyScope"]
                        || user["role"] == "Admin"
                        || user["isActive"] != true
                    {
                        return Err(invalid("请选择本公司启用的普通账号。"));
                    }
                    if store::all(tx, "people")?
                        .iter()
                        .any(|p| p["id"] != id && p["account"]["id"] == user_id)
                    {
                        return Err(conflict("账号已经关联其他人员。"));
                    }
                    user["fullName"] = person["profile"]["fullName"].clone();
                    user["departmentId"] = person["departmentId"].clone();
                    let username = records::text(&user, "username");
                    let user = store::save(
                        tx,
                        "users",
                        user_id,
                        user,
                        Some(username),
                        actor,
                        "personnel-link",
                    )?;
                    person["account"] = contracts::overlay(
                        contracts::initial(contracts::schema("PersonnelAccountRecord")),
                        &user,
                    );
                    save(tx, actor, id, person, "account-link", "")
                })?
            }
        }
        _ => return Err(invalid("人员操作无效。")),
    };
    serde_json::to_vec(&contracts::project(
        contracts::response(operation.id),
        result,
    ))
    .map_err(Into::into)
}
