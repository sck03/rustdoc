//! Template ownership, publication, sharing and version queries.
mod mutations;
pub(crate) mod policy;
pub(super) mod queries;
pub(super) mod starter;
use super::{
    auth,
    error::{Result, error, invalid, unavailable},
    records::text,
    report_assets,
    store::{self, Actor},
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
pub(super) fn demand_type(actor: &Actor, kind: &str) -> Result<()> {
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
    if content.is_empty() || content.len() > 10 * 1024 * 1024 {
        return Err(invalid("报表模板内容不能为空或超过 10 MiB。"));
    }
    let design = Design::from_source(content)
        .map_err(|message| invalid(format!("仅支持统一 V3 JSON 模板：{message}")))?;
    validate_design(kind, design)
}

pub fn validate_bytes(kind: &str, content: &[u8]) -> Result<Design> {
    if content.is_empty() || content.len() > 10 * 1024 * 1024 {
        return Err(invalid("报表模板内容不能为空或超过 10 MiB。"));
    }
    let design = export_doc_domain::report_template_format::decode(content).map_err(invalid)?;
    validate_design(kind, design)
}

fn validate_design(kind: &str, design: Design) -> Result<Design> {
    if design.report_type != report_type(kind)? {
        return Err(invalid("模板与单据的数据域不一致。"));
    }
    template::validate(&design, &field_catalog(&fields(kind)?)).map_err(invalid)?;
    Ok(design)
}

/// API content is the editable V3 JSON document. Managed `.dtpl` files stay a
/// binary container on disk and are decoded at this boundary.
pub fn editable_content(kind: &str, content: &[u8]) -> Result<String> {
    let design = validate_bytes(kind, content)?;
    serde_json::to_string_pretty(&design).map_err(Into::into)
}

/// Converts API-supplied V3 text back to the managed `.dtpl` container without
/// changing the shared OpenAPI response shape.
pub fn stored_content(kind: &str, content: &str) -> Result<Vec<u8>> {
    let design = validate_content(kind, content)?;
    export_doc_domain::report_template_format::encode(&design)
        .map_err(|message| invalid(format!("模板容器编码失败:{message}")))
}

fn record(actor: &Actor, value: &Value, content: bool, settings: &Value) -> Value {
    let state = text(value, "status");
    let protected = policy::is_global_reference(settings, value);
    let allowed = |action| !protected && policy::can_manage(actor, value, action);
    let mut output = json!({
        "id":value["id"],"reportType":value["reportType"],"name":value["name"],"status":value["status"],
        "shareScope":value["shareScope"],"versionNumber":value["versionNumber"],"ownerUserId":value["ownerUserId"],
        "canEdit":state!="Archived"&&value["shareScope"]=="Private"&&allowed("design"),
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
        return queries::list(service, actor, query_values);
    }
    if operation == GET_USER_REPORT_TEMPLATE || operation == LIST_USER_REPORT_TEMPLATE_VERSIONS {
        let id = super::records::id(parameters)?;
        if operation == LIST_USER_REPORT_TEMPLATE_VERSIONS {
            return queries::versions(service, actor, id, query_values);
        }
        let value = store.get(KIND, id)?;
        visible(actor, &value)?;
        return Ok(record(
            actor,
            &value,
            true,
            &store.settings("settings")?.unwrap_or_default(),
        ));
    }
    let _access = super::report_template_files::storage_lock(&service.paths)?;
    let saved = if [
        CREATE_USER_REPORT_TEMPLATE,
        SAVE_USER_REPORT_TEMPLATE_DRAFT,
        CLONE_USER_REPORT_TEMPLATE,
    ]
    .contains(&operation)
    {
        mutations::save(service, actor, operation, parameters, body)?
    } else {
        mutations::lifecycle(service, actor, operation, parameters, query_values, body)?
    };
    Ok(record(
        actor,
        &saved,
        true,
        &store.settings("settings")?.unwrap_or_default(),
    ))
}
