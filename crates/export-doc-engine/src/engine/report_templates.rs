//! Template ownership, publication, sharing and version queries.
mod mutations;
pub(super) mod starter;
use super::{
    auth,
    error::{Result, error, invalid, unavailable},
    records::text,
    report_assets,
    store::{self, Actor, Store},
};
use crate::{
    designer::{Design, field_catalog},
    generated_api::*,
    template,
};
use serde_json::{Value, json};

const KIND: &str = "report-templates";
const PERMISSION: &str = "document.report-templates";
pub const OPERATIONS: &[Operation] = &[
    LIST_USER_REPORT_TEMPLATES,
    GET_USER_REPORT_TEMPLATE,
    CREATE_USER_REPORT_TEMPLATE,
    SAVE_USER_REPORT_TEMPLATE_DRAFT,
    CLONE_USER_REPORT_TEMPLATE,
    PUBLISH_USER_REPORT_TEMPLATE,
    SHARE_USER_REPORT_TEMPLATE,
    DISABLE_USER_REPORT_TEMPLATE,
    RESTORE_USER_REPORT_TEMPLATE,
    ARCHIVE_USER_REPORT_TEMPLATE,
    LIST_USER_REPORT_TEMPLATE_VERSIONS,
    RESTORE_USER_REPORT_TEMPLATE_VERSION,
];

pub fn report_type(value: &str) -> Result<&'static str> {
    match value {
        "" | "ExportDocument" => Ok("ExportDocument"),
        "PaymentVoucher" => Ok("PaymentVoucher"),
        _ => Err(invalid("报表数据域无效。")),
    }
}
fn query<'a>(values: &'a [(&str, String)], name: &str) -> &'a str {
    values
        .iter()
        .find(|(key, _)| *key == name)
        .map(|(_, value)| value.as_str())
        .unwrap_or("")
}
fn demand_type(actor: &Actor, kind: &str) -> Result<()> {
    auth::authorize(
        actor,
        if report_type(kind)? == "PaymentVoucher" {
            "document.payments"
        } else {
            "document.invoices"
        },
        "view",
    )
}
pub fn fields(kind: &str) -> Result<ApiReportTemplateFieldCatalogResponse> {
    serde_json::from_str(if report_type(kind)? == "PaymentVoucher" {
        include_str!("../../resources/report-fields-payment.json")
    } else {
        include_str!("../../resources/report-fields.json")
    })
    .map_err(Into::into)
}
pub fn validate_content(kind: &str, content: &str) -> Result<Design> {
    if content.trim().is_empty() || content.len() > 4 * 1024 * 1024 {
        return Err(invalid("报表模板内容不能为空或超过 4 MiB。"));
    }
    let design = Design::from_html(content).map_err(invalid)?;
    if design.report_type != report_type(kind)? {
        return Err(invalid("模板与单据的数据域不一致。"));
    }
    template::validate(&design, &field_catalog(&fields(kind)?)).map_err(invalid)?;
    Ok(design)
}

fn record(actor: &Actor, value: &Value, content: bool) -> Value {
    let state = text(value, "status");
    let allowed = |action| auth::visible(actor, PERMISSION, action, value);
    let mut output = json!({
        "id":value["id"],"reportType":value["reportType"],"name":value["name"],"status":value["status"],
        "shareScope":value["shareScope"],"versionNumber":value["versionNumber"],"ownerUserId":value["ownerUserId"],
        "canEdit":state!="Archived"&&value["ownerUserId"]==actor.id&&allowed("design"),
        "canPublish":state=="Draft"&&allowed("publish"),
        "canShare":(["Published","Disabled"].contains(&state.as_str())&&allowed("share")),
        "canDisable":state=="Published"&&allowed("deactivate"),
        "canRestore":(["Disabled","Archived"].contains(&state.as_str())&&allowed("restore")),
        "canArchive":state!="Archived"&&allowed("archive")
    });
    if content {
        output["contentHtml"] = value["contentHtml"].clone();
    }
    output
}
fn visible(actor: &Actor, value: &Value) -> Result<()> {
    demand_type(actor, &text(value, "reportType"))?;
    if !report_assets::template_visible(actor, value) {
        return Err(error(403, "没有读取此报表模板的权限。"));
    }
    Ok(())
}

pub fn handle(
    service: &crate::engine::NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query_values: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let store = &service.store;
    if operation == LIST_USER_REPORT_TEMPLATES {
        let kind = report_type(query(query_values, "reportType"))?;
        demand_type(actor, kind)?;
        let include_archived = query(query_values, "includeArchived") == "true";
        let keyword = query(query_values, "keyword");
        if keyword.chars().count() > 150 {
            return Err(invalid("模板搜索名称不能超过 150 个字符。"));
        }
        let keyword = store::normalize(keyword);
        let mut values: Vec<_> = store
            .all(KIND)?
            .into_iter()
            .filter(|value| {
                value["reportType"] == kind
                    && (include_archived || value["status"] != "Archived")
                    && report_assets::template_visible(actor, value)
                    && store::normalize(&text(value, "name")).contains(&keyword)
            })
            .collect();
        values.sort_by(|left, right| {
            (right["status"] == "Published")
                .cmp(&(left["status"] == "Published"))
                .then_with(|| text(left, "name").cmp(&text(right, "name")))
                .then_with(|| left["id"].as_i64().cmp(&right["id"].as_i64()))
        });
        return Ok(store::paged(
            values
                .iter()
                .map(|value| record(actor, value, false))
                .collect(),
            query_values,
        ));
    }
    if operation == GET_USER_REPORT_TEMPLATE || operation == LIST_USER_REPORT_TEMPLATE_VERSIONS {
        let id = super::records::id(parameters)?;
        let value = store.get(KIND, id)?;
        visible(actor, &value)?;
        if operation == GET_USER_REPORT_TEMPLATE {
            return Ok(record(actor, &value, true));
        }
        let mut versions = vec![];
        for version in store
            .all("template-versions")?
            .into_iter()
            .filter(|v| v["templateKind"] == KIND && v["templateId"] == id)
        {
            let content = &version["content"];
            if !content.is_object() {
                return Err(unavailable("模板历史内容损坏。"));
            }
            versions.push(json!({"id":version["id"],"userReportTemplateId":id,"versionNumber":content["versionNumber"],"changeType":version["changeType"],"name":content["name"],"status":content["status"],"shareScope":content["shareScope"],"changedBy":version["changedBy"],"createdAt":version["createdAt"],"canRestore":auth::visible(actor,PERMISSION,"restore",&value)}));
        }
        versions.sort_by_key(|v| std::cmp::Reverse(v["versionNumber"].as_i64().unwrap_or(0)));
        return Ok(store::paged(versions, query_values));
    }
    let saved = if [
        CREATE_USER_REPORT_TEMPLATE,
        SAVE_USER_REPORT_TEMPLATE_DRAFT,
        CLONE_USER_REPORT_TEMPLATE,
    ]
    .contains(&operation)
    {
        mutations::save(service, actor, operation, parameters, body)?
    } else {
        mutations::lifecycle(store, actor, operation, parameters, query_values, body)?
    };
    Ok(record(actor, &saved, true))
}
