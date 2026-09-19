//! Email template ownership, shared audiences, preview and immutable versions.
use super::{
    NativeService, auth,
    error::{Result, conflict, error, invalid, unavailable},
    records::{id, text},
    store::{self, Actor},
};
use crate::{contracts, generated_api::*};
use export_doc_domain::template_lifecycle::{self, Action, ShareScope, Status};
use export_doc_mail::content;
use export_doc_storage::Connection;
use serde_json::{Value, json};
const KIND: &str = "email-templates";
const PERMISSION: &str = "sales.email-templates";
pub const OPERATIONS: &[Operation] = &[
    LIST_EMAIL_TEMPLATES,
    CREATE_EMAIL_TEMPLATE,
    SAVE_EMAIL_TEMPLATE_DRAFT,
    LIST_EMAIL_TEMPLATE_VARIABLES,
    PREVIEW_EMAIL_TEMPLATE,
    LIST_EMAIL_TEMPLATE_VERSIONS,
    PUBLISH_EMAIL_TEMPLATE,
    SHARE_EMAIL_TEMPLATE,
    DISABLE_EMAIL_TEMPLATE,
    RESTORE_EMAIL_TEMPLATE,
    ARCHIVE_EMAIL_TEMPLATE,
    RESTORE_EMAIL_TEMPLATE_VERSION,
    GET_CRM_EMAIL_VARIABLE_DRAFT,
];
fn dto(actor: &Actor, row: &Value) -> Value {
    let mut value = contracts::project(contracts::schema("ApiEmailTemplateDto"), row.clone());
    let status = text(row, "status");
    let can = |action| auth::visible(actor, PERMISSION, action, row);
    for (key, enabled) in [
        (
            "canEdit",
            row["ownerUserId"] == actor.id && status != "Archived" && can("edit"),
        ),
        ("canPublish", status == "Draft" && can("publish")),
        (
            "canShare",
            ["Published", "Disabled"].contains(&status.as_str()) && can("share"),
        ),
        ("canDisable", status == "Published" && can("deactivate")),
        (
            "canRestore",
            ["Disabled", "Archived"].contains(&status.as_str()) && can("restore"),
        ),
        ("canArchive", status != "Archived" && can("archive")),
    ] {
        value[key] = json!(enabled);
    }
    value
}
fn transition(row: &mut Value, action: Action) -> Result<()> {
    let status: Status =
        serde_json::from_value(row["status"].clone()).map_err(|_| unavailable("模板状态损坏。"))?;
    let scope: ShareScope = serde_json::from_value(row["shareScope"].clone())
        .map_err(|_| unavailable("模板共享范围损坏。"))?;
    let (status, scope) =
        template_lifecycle::transition(status, scope, action).map_err(conflict)?;
    row["status"] = json!(status);
    row["shareScope"] = json!(scope);
    Ok(())
}
fn persist(tx: &Connection, actor: &Actor, id: i64, mut row: Value, action: &str) -> Result<Value> {
    if id == 0 {
        row["ownerUserId"] = json!(actor.id);
    }
    let name = store::normalize(&text(&row, "name"));
    let category = store::normalize(&text(&row, "category"));
    if row["status"] != "Archived"
        && store::all(tx, KIND)?.iter().any(|r| {
            r["id"].as_i64() != Some(id)
                && r["ownerUserId"] == row["ownerUserId"]
                && r["status"] != "Archived"
                && store::normalize(&text(r, "name")) == name
                && store::normalize(&text(r, "category")) == category
        })
    {
        return Err(conflict("同一分类下已经有同名邮件模板。"));
    }
    let saved = store::save(tx, KIND, id, row, None, actor, action)?;
    store::save(
        tx,
        "template-versions",
        0,
        json!({"templateKind":KIND,"templateId":saved["id"],"content":saved,"changeType":action,"changedBy":actor.name}),
        None,
        actor,
        "version",
    )?;
    Ok(saved)
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    if operation == GET_CRM_EMAIL_VARIABLE_DRAFT {
        return crm_draft(service, actor, parameters);
    }
    if operation == LIST_EMAIL_TEMPLATE_VARIABLES {
        return Ok(json!(content::VARIABLES.iter().map(|(key,label,sample)|json!({"key":key,"token":format!("{{{{{key}}}}}"),"label":label,"sampleValue":sample})).collect::<Vec<_>>()));
    }
    if operation == PREVIEW_EMAIL_TEMPLATE {
        let variables =
            serde_json::from_value(body.get("variables").cloned().unwrap_or_else(|| json!({})))
                .map_err(|_| invalid("模板变量必须是文本字典。"))?;
        let preview = content::preview(&text(body, "subject"), &text(body, "bodyHtml"), &variables)
            .map_err(invalid)?;
        return Ok(
            json!({"subject":preview.subject,"bodyHtml":preview.html,"unresolvedTokens":preview.unresolved}),
        );
    }
    service.store.transaction(|tx| {
        if operation == LIST_EMAIL_TEMPLATES {
            let archived = super::hs::read(query, "includeArchived") == "true";
            if archived {
                auth::authorize(actor, PERMISSION, "restore")?;
            }
            let keyword = store::normalize(super::hs::read(query, "keyword"));
            let category = super::hs::read(query, "category");
            if keyword.chars().count() > 150 || category.chars().count() > 50 {
                return Err(invalid("模板检索条件过长。"));
            }
            let mut rows = store::all(tx, KIND)?;
            rows.retain(|row| {
                auth::template_visible(actor, PERMISSION, row)
                    && (archived || row["status"] != "Archived")
                    && (category.is_empty() || row["category"] == category)
                    && ["name", "subject", "bodyHtml"]
                        .iter()
                        .any(|k| store::normalize(&text(row, k)).contains(&keyword))
            });
            rows.sort_by_key(|r| (text(r, "category"), text(r, "name")));
            return Ok(json!(
                rows.iter().map(|r| dto(actor, r)).collect::<Vec<_>>()
            ));
        }
        let record_id = if operation == CREATE_EMAIL_TEMPLATE {
            0
        } else {
            id(parameters)?
        };
        let mut row = if record_id == 0 {
            json!({"id":0,"status":"Draft","shareScope":"Private","ownerUserId":actor.id})
        } else {
            store::get(tx, KIND, record_id)?
        };
        if operation == LIST_EMAIL_TEMPLATE_VERSIONS {
            if !auth::template_visible(actor, PERMISSION, &row) {
                return Err(error(403, "没有查看此邮件模板的权限。"));
            }
            let mut versions = vec![];
            for version in store::all(tx, "template-versions")?
                .into_iter()
                .filter(|r| r["templateKind"] == KIND && r["templateId"] == record_id)
            {
                let mut value = version["content"].clone();
                if !value.is_object() {
                    return Err(unavailable("邮件模板历史损坏。"));
                }
                for key in ["id", "changeType", "changedBy", "createdAt"] {
                    value[key] = version[key].clone();
                }
                value["emailTemplateId"] = json!(record_id);
                value["canRestore"] = json!(auth::visible(actor, PERMISSION, "restore", &row));
                versions.push(contracts::project(
                    contracts::schema("ApiEmailTemplateVersionDto"),
                    value,
                ));
            }
            versions.sort_by_key(|r| std::cmp::Reverse(r["versionNumber"].as_i64().unwrap_or(0)));
            return Ok(json!(versions));
        }
        let action = match operation {
            CREATE_EMAIL_TEMPLATE | SAVE_EMAIL_TEMPLATE_DRAFT => "edit",
            PUBLISH_EMAIL_TEMPLATE => "publish",
            SHARE_EMAIL_TEMPLATE => "share",
            DISABLE_EMAIL_TEMPLATE => "deactivate",
            RESTORE_EMAIL_TEMPLATE | RESTORE_EMAIL_TEMPLATE_VERSION => "restore",
            ARCHIVE_EMAIL_TEMPLATE => "archive",
            _ => return Err(invalid("邮件模板操作无效。")),
        };
        auth::authorize(actor, PERMISSION, action)?;
        if record_id > 0 {
            if !auth::visible(actor, PERMISSION, action, &row)
                || (action == "edit" && row["ownerUserId"] != actor.id)
            {
                return Err(error(403, "没有修改此邮件模板的权限。"));
            }
            store::check_version(&row, store::expected(body))?;
        } else if store::expected(body) != 0 {
            return Err(invalid("新增模板不能包含已有版本号。"));
        }
        if action == "edit" {
            for (key, label, max, required) in [
                ("name", "模板名称", 150, true),
                ("category", "分类", 50, false),
                ("subject", "邮件主题", 300, false),
                ("bodyHtml", "模板正文", 10000, false),
            ] {
                row[key] = json!(
                    export_doc_domain::crm::text(&text(body, key), label, max, required)
                        .map_err(invalid)?
                );
            }
            if text(&row, "category").is_empty() {
                row["category"] = json!("通用");
            }
            let html = content::sanitize(&text(&row, "bodyHtml"))
                .map_err(invalid)?
                .0;
            if html.chars().count() > 10000 {
                return Err(invalid("邮件正文净化后超过 10000 字。"));
            }
            row["bodyHtml"] = json!(html);
            transition(&mut row, Action::SaveDraft)?;
        } else if operation == RESTORE_EMAIL_TEMPLATE_VERSION {
            let version = super::hs::read(parameters, "versionNumber")
                .parse::<i64>()
                .ok()
                .filter(|v| *v > 0)
                .ok_or_else(|| invalid("历史版本号无效。"))?;
            if row["versionNumber"] == version {
                return Ok(dto(actor, &row));
            }
            let historical = store::all(tx, "template-versions")?
                .into_iter()
                .find(|v| {
                    v["templateKind"] == KIND
                        && v["templateId"] == record_id
                        && v["content"]["versionNumber"] == version
                })
                .ok_or_else(|| error(404, "邮件模板历史版本不存在。"))?;
            for key in ["name", "category", "subject", "bodyHtml"] {
                row[key] = historical["content"][key].clone();
            }
            row["bodyHtml"] = json!(
                content::sanitize(&text(&row, "bodyHtml"))
                    .map_err(invalid)?
                    .0
            );
            transition(&mut row, Action::RestoreVersion)?;
        } else {
            let action = match operation {
                PUBLISH_EMAIL_TEMPLATE => Action::Publish,
                DISABLE_EMAIL_TEMPLATE => Action::Disable,
                RESTORE_EMAIL_TEMPLATE => Action::Restore,
                ARCHIVE_EMAIL_TEMPLATE => Action::Archive,
                SHARE_EMAIL_TEMPLATE => {
                    let scope: ShareScope = serde_json::from_value(body["shareScope"].clone())
                        .map_err(|_| invalid("模板共享范围无效。"))?;
                    if scope == ShareScope::Company && actor.company.is_empty()
                        || scope == ShareScope::Department
                            && (actor.company.is_empty() || actor.department.is_empty())
                    {
                        return Err(invalid("请先设置账号所属公司或部门。"));
                    }
                    Action::Share(scope)
                }
                _ => return Err(invalid("邮件模板操作无效。")),
            };
            transition(&mut row, action)?;
        }
        row["expectedVersion"] = json!(store::expected(body));
        persist(tx, actor, record_id, row, action).map(|saved| dto(actor, &saved))
    })
}
fn crm_draft(
    service: &NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<Value> {
    let customer_id = super::hs::read(parameters, "customerId")
        .parse::<i64>()
        .ok()
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("客户编号无效。"))?;
    service.store.transaction(|tx|{
        let customer=store::get(tx,"crm-customers",customer_id)?;if !auth::visible(actor,"sales.customers","view",&customer){return Err(error(403,"没有读取此客户的权限。"));}
        let mut contacts:Vec<_>=store::all(tx,"crm-contacts")?.into_iter().filter(|r|r["crmCustomerId"]==customer_id&&auth::visible(actor,"sales.contacts","view",r)).collect();
        contacts.sort_by_key(|r|(r["isPrimary"]!=true,r["id"].as_i64().unwrap_or(0)));let contact=contacts.first();
        let company=store::all(tx,"companies")?.into_iter().find(|r|r["code"]==actor.company&&r["isActive"]==true).map(|r|text(&r,"name")).unwrap_or_default();
        Ok(json!({"crmCustomerId":customer_id,"crmContactId":contact.map(|r|r["id"].clone()),"toAddress":contact.map(|r|text(r,"email")).unwrap_or_default(),
            "variables":{"CustomerName":customer["name"],"ContactName":contact.map(|r|text(r,"name")).unwrap_or_default(),"CompanyName":company,"ProductName":"","QuotationNo":"","SenderName":actor.name,"Today":service.clock.now().map_err(unavailable)?.today.to_string()}}))
    })
}
