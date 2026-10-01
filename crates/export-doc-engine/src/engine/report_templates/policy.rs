//! Shared template authorization and global-default integrity.
use super::*;
use export_doc_storage::Connection;

pub(crate) fn usable(actor: &Actor, value: &Value) -> bool {
    (["Draft", "Published"].contains(&text(value, "status").as_str())
        && value["ownerUserId"] == actor.id)
        || (value["status"] == "Published" && value["shareScope"] != "Private")
}

pub(super) fn can_manage(actor: &Actor, value: &Value, action: &str) -> bool {
    (actor.admin || value["ownerUserId"] == actor.id)
        && auth::visible(actor, PERMISSION, action, value)
}

pub(super) fn demand_private_edit(value: &Value) -> Result<()> {
    if value["shareScope"] != "Private" {
        return Err(super::super::error::conflict(
            "共享模板保持只读。请复制后编辑；或先明确收回共享，再修改原模板。",
        ));
    }
    Ok(())
}

pub(crate) fn validate_default(tx: &Connection, kind: &str, path: &str) -> Result<Option<Value>> {
    let Some(id) = path.trim().strip_prefix("user-template:") else {
        return Ok(None);
    };
    let id = id
        .parse::<i64>()
        .ok()
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("模板编号无效。"))?;
    let value = store::get(tx, KIND, id)?;
    if value["reportType"] != kind || value["status"] != "Published" || value["shareScope"] != "All"
    {
        return Err(invalid(
            "全局默认必须是已发布且对全体团队成员共享的模板；个人模板请在输出时选择。",
        ));
    }
    Ok(Some(value))
}

pub(crate) fn global_references(settings: &Value) -> Vec<(&str, &str)> {
    let mut paths = vec![];
    for (key, kind) in [
        ("exportDocumentTemplatePath", "ExportDocument"),
        ("paymentVoucherTemplatePath", "PaymentVoucher"),
    ] {
        if let Some(path) = settings["reportTemplateDefaults"][key].as_str() {
            paths.push((kind, path));
        }
    }
    for (items, kind) in [
        (&settings["batchExport"]["items"], "ExportDocument"),
        (&settings["paymentTemplates"], "PaymentVoucher"),
    ] {
        for item in items.as_array().into_iter().flatten() {
            if let Some(path) = item["templatePath"].as_str() {
                paths.push((kind, path));
            }
        }
    }
    paths
}

pub(super) fn is_global_reference(settings: &Value, value: &Value) -> bool {
    global_references(settings).iter().any(|(_, path)| {
        path.trim()
            .strip_prefix("user-template:")
            .and_then(|id| id.parse::<i64>().ok())
            == value["id"].as_i64()
    })
}

pub(super) fn protect_default(tx: &Connection, actor: &Actor, value: &Value) -> Result<()> {
    let Some(settings) = tx.settings("settings")? else {
        return Ok(());
    };
    if is_global_reference(&settings, value) {
        if !actor.admin {
            return Err(error(403, "全局默认模板仅管理员可维护，请复制为个人模板。"));
        }
        if value["status"] != "Published" || value["shareScope"] != "All" {
            return Err(super::super::error::conflict(
                "请先更换全局默认模板，再修改、停用、归档或收回共享；也可复制后编辑。",
            ));
        }
    }
    Ok(())
}
