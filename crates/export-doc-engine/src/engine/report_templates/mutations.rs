use super::*;
use crate::engine::{error::conflict, records::required, store::Connection};
use export_doc_domain::template_lifecycle::{self, Action, ShareScope, Status};

fn history(tx: &Connection, actor: &Actor, saved: &Value, action: &str) -> Result<()> {
    store::save(
        tx,
        "template-versions",
        0,
        json!({"templateKind":KIND,"templateId":saved["id"],"content":saved,"changeType":action,"changedBy":actor.name}),
        None,
        actor,
        "version",
    )?;
    Ok(())
}
fn unique(tx: &Connection, value: &Value) -> Result<()> {
    if value["status"] == "Archived" {
        return Ok(());
    }
    let name = store::normalize(&text(value, "name"));
    if tx.all(KIND)?.iter().any(|other| {
        other["id"] != value["id"]
            && other["ownerUserId"] == value["ownerUserId"]
            && other["reportType"] == value["reportType"]
            && other["status"] != "Archived"
            && store::normalize(&text(other, "name")) == name
    }) {
        return Err(conflict("你已经拥有同名报表模板。"));
    }
    Ok(())
}
fn transition(value: &mut Value, action: Action) -> Result<()> {
    let state: Status = serde_json::from_value(value["status"].clone())
        .map_err(|_| unavailable("模板发布状态损坏。"))?;
    let scope: ShareScope = serde_json::from_value(value["shareScope"].clone())
        .map_err(|_| unavailable("模板共享范围损坏。"))?;
    let (state, scope) = template_lifecycle::transition(state, scope, action).map_err(conflict)?;
    value["status"] = json!(state);
    value["shareScope"] = json!(scope);
    Ok(())
}
fn persist(tx: &Connection, actor: &Actor, id: i64, value: Value, action: &str) -> Result<Value> {
    unique(tx, &value)?;
    report_assets::validate_template(tx, actor, &text(&value, "contentHtml"))?;
    let saved = store::save(tx, KIND, id, value, None, actor, action)?;
    history(tx, actor, &saved, action)?;
    Ok(saved)
}

pub(super) fn save(
    service: &crate::engine::NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let store = &service.store;
    let kind = report_type(&text(body, "reportType"))?;
    demand_type(actor, kind)?;
    let permission = if operation == CLONE_USER_REPORT_TEMPLATE {
        "clone"
    } else {
        "design"
    };
    auth::authorize(actor, PERMISSION, permission)?;
    required(body, "name", "模板名称", 160)?;
    let id = if operation == SAVE_USER_REPORT_TEMPLATE_DRAFT {
        super::super::records::id(parameters)?
    } else {
        0
    };
    let cloned_file_content = if operation == CLONE_USER_REPORT_TEMPLATE {
        let path = text(body, "sourceTemplatePath");
        if path
            .strip_prefix("user-template:")
            .and_then(|id| id.parse::<i64>().ok())
            .filter(|id| *id > 0)
            .is_some()
        {
            None
        } else {
            let (_, content) = super::super::report_template_files::load_template_content(
                service, actor, kind, &path,
            )?;
            Some(content)
        }
    } else {
        None
    };
    store.transaction(|tx| {
        let content = if operation == CLONE_USER_REPORT_TEMPLATE {
            let path = text(body, "sourceTemplatePath");
            if let Some(source_id) = path.strip_prefix("user-template:").and_then(|id| id.parse::<i64>().ok()).filter(|id| *id > 0) {
                let source = store::get(tx, KIND, source_id)?;
                visible(actor, &source)?;
                if source["reportType"] != kind { return Err(invalid("不能跨数据域复制模板。")); }
                text(&source, "contentHtml")
            } else {
                cloned_file_content.clone().ok_or_else(|| invalid("文件模板复制内容缺失。"))?
            }
        } else { text(body, "contentHtml") };
        validate_content(kind, &content)?;
        let mut value = if id > 0 {
            let value = store::get(tx, KIND, id)?;
            if value["ownerUserId"] != actor.id || !auth::visible(actor, PERMISSION, "design", &value) { return Err(error(403, "只能编辑自己拥有的模板，请先复制为个人草稿。")); }
            if value["reportType"] != kind { return Err(invalid("不能修改报表模板的数据域。")); }
            store::check_version(&value, store::expected(body))?;
            value
        } else { json!({"id":0,"reportType":kind,"status":"Draft","shareScope":"Private","ownerUserId":actor.id,"companyScope":actor.company,"departmentId":actor.department}) };
        let previous = value.clone();
        transition(&mut value, Action::SaveDraft)?;
        value["name"] = json!(text(body, "name"));
        value["contentHtml"] = json!(content);
        if id > 0 && value == previous { return Ok(value); }
        persist(tx, actor, id, value, if operation == CLONE_USER_REPORT_TEMPLATE { "复制草稿" } else { "保存草稿" })
    })
}

pub(super) fn lifecycle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    queries: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let id = super::super::records::id(parameters)?;
    let (permission, action, label) = match operation {
        PUBLISH_USER_REPORT_TEMPLATE => ("publish", Action::Publish, "发布"),
        SHARE_USER_REPORT_TEMPLATE => {
            let scope = ["Private", "Department", "Company", "All"]
                .into_iter()
                .find(|scope| scope.eq_ignore_ascii_case(&text(body, "shareScope")))
                .ok_or_else(|| invalid("报表模板共享范围无效。"))?;
            let scope: ShareScope = serde_json::from_value(json!(scope))?;
            if matches!(scope, ShareScope::Company | ShareScope::Department)
                && actor.company.is_empty()
            {
                return Err(invalid("当前账号未归属公司，不能设置公司或部门共享。"));
            }
            if scope == ShareScope::Department && actor.department.is_empty() {
                return Err(invalid("当前账号未归属部门，不能设置部门共享。"));
            }
            ("share", Action::Share(scope), "调整共享范围")
        }
        DISABLE_USER_REPORT_TEMPLATE => ("deactivate", Action::Disable, "停用"),
        RESTORE_USER_REPORT_TEMPLATE => ("restore", Action::Restore, "恢复"),
        ARCHIVE_USER_REPORT_TEMPLATE => ("archive", Action::Archive, "归档"),
        RESTORE_USER_REPORT_TEMPLATE_VERSION => ("restore", Action::RestoreVersion, "恢复历史内容"),
        _ => return Err(invalid("未知的模板生命周期操作。")),
    };
    auth::authorize(actor, PERMISSION, permission)?;
    store.transaction(|tx| {
        let mut value = store::get(tx, KIND, id)?;
        demand_type(actor, &text(&value, "reportType"))?;
        if !auth::visible(actor, PERMISSION, permission, &value) {
            return Err(error(403, "没有办理此报表模板的权限。"));
        }
        let expected = if operation == ARCHIVE_USER_REPORT_TEMPLATE {
            query(queries, "expectedVersion")
                .parse()
                .unwrap_or_else(|_| store::expected(body))
        } else {
            store::expected(body)
        };
        store::check_version(&value, expected)?;
        if operation == RESTORE_USER_REPORT_TEMPLATE_VERSION {
            let number: i64 = query(parameters, "versionNumber")
                .parse()
                .ok()
                .filter(|number| *number > 0)
                .ok_or_else(|| invalid("报表历史版本号无效。"))?;
            if value["versionNumber"] == number {
                return Ok(value);
            }
            let source = tx
                .all("template-versions")?
                .into_iter()
                .find(|v| {
                    v["templateKind"] == KIND
                        && v["templateId"] == id
                        && v["content"]["versionNumber"] == number
                })
                .ok_or_else(|| error(404, "报表模板历史版本不存在。"))?;
            value["name"] = source["content"]["name"].clone();
            value["contentHtml"] = source["content"]["contentHtml"].clone();
        }
        transition(&mut value, action)?;
        validate_content(&text(&value, "reportType"), &text(&value, "contentHtml"))?;
        persist(tx, actor, id, value, label)
    })
}
