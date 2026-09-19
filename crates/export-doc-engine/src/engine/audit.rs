//! Audit queries and maintenance never delete or query business history.
#[allow(unused_imports)]
use super::error::error;
use super::{
    NativeService, auth,
    error::{Result, invalid, unavailable},
    records::text,
    store::Actor,
};
use crate::{contracts, generated_api::*};
use chrono::{DateTime, SecondsFormat, Utc};
use export_doc_storage::{AuditWrite, Connection};
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[
    LIST_AUDIT_LOGS,
    DELETE_AUDIT_LOGS_BY_CRITERIA,
    CLEANUP_AUDIT_LOGS,
];
#[cfg(feature = "excel")]
pub const EXPORTS: &[Operation] = &[DOWNLOAD_AUDIT_LOGS, SAVE_AUDIT_LOGS_TO_PATH];
pub const ENTITIES: &[(&str, &str, &str)] = &[
    ("invoices", "Invoice", "发票"),
    ("customers", "Customer", "单证客户"),
    ("exporters", "Exporter", "出口商"),
    ("payments", "Payment", "付款报销"),
    ("payees", "Payee", "收款对象"),
    ("products", "Product", "商品资料"),
    ("hs-codes", "HsCode", "HS 编码"),
    ("users", "User", "系统账号"),
    ("permission-templates", "PermissionTemplate", "权限方案"),
    ("report-templates", "UserReportTemplate", "用户报表模板"),
    (
        "report-template-versions",
        "UserReportTemplateVersion",
        "模板版本",
    ),
    ("people", "Personnel", "人员档案"),
    ("bookings", "MeetingBooking", "会议室预约"),
    ("supply-requests", "OfficeSupplyRequest", "物品领用"),
    ("crm-customers", "CrmCustomer", "CRM 客户"),
    ("crm-contacts", "CrmContact", "客户联系人"),
    ("crm-follow-ups", "CrmFollowUp", "客户跟进"),
    ("opportunities", "SalesOpportunity", "商机与报价"),
    (
        "opportunity-events",
        "SalesOpportunityHistory",
        "商机版本历史",
    ),
    ("suppliers", "SupplierCompany", "供应商"),
    ("supplier-contacts", "SupplierContact", "供应商联系人"),
    ("supplier-products", "SupplierProductLink", "供应产品"),
    ("supplier-assessments", "SupplierAssessment", "供应商评价"),
    ("attachments", "BusinessAttachment", "业务资料"),
    (
        "attachment-categories",
        "BusinessAttachmentCategory",
        "资料分类",
    ),
];
const POLICY: &str = "仅查询、导出和清理独立审计记录；原业务数据与处理历史保持完整。";
const FILTERS: &[&str] = &[
    "invoiceKeyword",
    "entityName",
    "action",
    "userId",
    "startTime",
    "endTime",
    "keyword",
];

#[derive(Default)]
struct Criteria {
    invoice: String,
    entity: String,
    action: String,
    user: String,
    keyword: String,
    start: Option<DateTime<Utc>>,
    end: Option<DateTime<Utc>>,
}
impl Criteria {
    fn parse(body: &Value) -> Result<Self> {
        let option = |key| {
            let value = text(body, key);
            if value == "全部" {
                String::new()
            } else {
                value
            }
        };
        let instant = |key| {
            let value = text(body, key);
            if value.is_empty() {
                Ok(None)
            } else {
                DateTime::parse_from_rfc3339(&value)
                    .map(|time| Some(time.with_timezone(&Utc)))
                    .map_err(|_| invalid("审计时间必须包含日期、时间和时区偏移。"))
            }
        };
        let criteria = Self {
            invoice: option("invoiceKeyword").to_lowercase(),
            entity: option("entityName"),
            action: option("action"),
            user: option("userId").to_lowercase(),
            keyword: option("keyword").to_lowercase(),
            start: instant("startTime")?,
            end: instant("endTime")?,
        };
        if criteria
            .start
            .zip(criteria.end)
            .is_some_and(|(start, end)| start > end)
        {
            return Err(invalid("审计结束时间不能早于开始时间。"));
        }
        if FILTERS
            .iter()
            .any(|key| text(body, key).chars().count() > 250)
        {
            return Err(invalid("筛选条件不能超过 250 字。"));
        }
        Ok(criteria)
    }
    fn has_filter(&self) -> bool {
        !self.invoice.is_empty()
            || !self.entity.is_empty()
            || !self.action.is_empty()
            || !self.user.is_empty()
            || !self.keyword.is_empty()
            || self.start.is_some()
            || self.end.is_some()
    }
    fn matches(&self, row: &Value) -> Result<bool> {
        let timestamp = DateTime::parse_from_rfc3339(&text(row, "timestamp"))
            .map_err(|_| unavailable("审计记录时间损坏，已停止处理。"))?
            .with_timezone(&Utc);
        let searchable = format!(
            "{} {} {} {} {}",
            text(row, "entityName"),
            text(row, "entityId"),
            text(row, "userId"),
            text(row, "oldValues"),
            text(row, "newValues")
        )
        .to_lowercase();
        let invoice_values = format!(
            "{} {} {}",
            if row["entityName"] == "Invoice" {
                text(row, "entityId")
            } else {
                String::new()
            },
            text(row, "oldValues"),
            text(row, "newValues")
        )
        .to_lowercase();
        Ok(
            (self.entity.is_empty() || self.entity == text(row, "entityName"))
                && (self.action.is_empty() || self.action == text(row, "action"))
                && text(row, "userId").to_lowercase().contains(&self.user)
                && searchable.contains(&self.keyword)
                && invoice_values.contains(&self.invoice)
                && self.start.is_none_or(|start| timestamp >= start)
                && self.end.is_none_or(|end| timestamp <= end),
        )
    }
}
fn project(event: Value) -> Value {
    let kind = text(&event, "kind");
    let entity = ENTITIES
        .iter()
        .find(|(key, _, _)| *key == kind)
        .map(|(_, name, _)| *name)
        .unwrap_or(&kind);
    let action = match text(&event, "action").as_str() {
        "delete" => "Deleted",
        "create" => "Added",
        _ => "Modified",
    };
    let old = event["body"]
        .get("oldValues")
        .cloned()
        .unwrap_or(json!({}))
        .to_string();
    let new = event["body"]
        .get("newValues")
        .cloned()
        .unwrap_or_else(|| json!({"versionNumber":event["versionNumber"]}))
        .to_string();
    let preview = |text: &str| text.chars().take(400).collect::<String>();
    json!({"id":event["id"],"entityName":entity,"entityId":event["recordId"].to_string(),
        "action":action,"userId":event["actorUserId"].to_string(),"timestamp":event["timestamp"],
        "oldValuesPreview":preview(&old),"newValuesPreview":preview(&new),"oldValues":old,"newValues":new})
}
fn select(
    tx: &Connection,
    criteria: &Criteria,
    offset: usize,
    limit: usize,
    count_all: bool,
) -> Result<(Vec<Value>, usize)> {
    let mut before = i64::MAX;
    let mut matched = 0usize;
    let mut selected = vec![];
    loop {
        crate::operation::check()?;
        let batch = tx.audit_events(before, 256)?;
        if batch.is_empty() {
            break;
        }
        for event in batch {
            before = event["id"]
                .as_i64()
                .filter(|id| *id > 0 && *id < before)
                .ok_or_else(|| unavailable("审计游标顺序异常。"))?;
            let row = project(event);
            if criteria.matches(&row)? {
                if matched >= offset && selected.len() < limit {
                    selected.push(row);
                }
                matched += 1;
                if !count_all && selected.len() >= limit {
                    return Ok((selected, matched));
                }
            }
        }
    }
    Ok((selected, matched))
}
fn command(count: usize, destination: &str, message: &str) -> Value {
    json!({"success":true,"message":format!("{message}（{count} 条）"),"affectedCount":count,"destinationPath":destination,"storagePolicy":POLICY})
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    auth::authorize(
        actor,
        "system.audit",
        if operation == LIST_AUDIT_LOGS {
            "view"
        } else {
            "manage"
        },
    )?;
    if operation == LIST_AUDIT_LOGS {
        let filters: Value = query
            .iter()
            .map(|(key, value)| ((*key).to_owned(), json!(value)))
            .collect();
        let criteria = Criteria::parse(&filters)?;
        let number = |key: &str, default: usize| {
            text(&filters, key)
                .parse::<usize>()
                .unwrap_or(default)
                .max(1)
        };
        let page = number("pageNumber", 1);
        let size = number("pageSize", 50).min(200);
        let (rows, total) = service.store.transaction(|tx| {
            select(
                tx,
                &criteria,
                page.saturating_sub(1).saturating_mul(size),
                size,
                true,
            )
        })?;
        return serde_json::to_vec(&contracts::page(rows, total, page, size)).map_err(Into::into);
    }
    #[cfg(feature = "excel")]
    if EXPORTS.contains(&operation) {
        let criteria = Criteria::parse(body)?;
        let limit = body["maxCount"]
            .as_u64()
            .filter(|value| *value > 0)
            .unwrap_or(50_000)
            .min(50_000) as usize;
        let (rows, _) = service
            .store
            .transaction(|tx| select(tx, &criteria, 0, limit, false))?;
        let bytes = export_doc_excel::table(
            &[
                ("id", "编号"),
                ("entityName", "实体"),
                ("entityId", "业务编号"),
                ("action", "操作"),
                ("userId", "操作人"),
                ("timestamp", "时间"),
                ("oldValues", "原值"),
                ("newValues", "新值"),
            ],
            &rows,
            &|| crate::operation::check().map_err(|error| error.to_string()),
        )
        .map_err(invalid)?;
        if operation == DOWNLOAD_AUDIT_LOGS {
            return Ok(bytes);
        }
        if service.provider()? != "SQLite" {
            return Err(error(403, "服务器不支持本机保存路径。"));
        }
        let path = std::path::PathBuf::from(text(body, "destinationPath"));
        crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
        if !path
            .file_name()
            .and_then(|name| name.to_str())
            .is_some_and(crate::paths::valid_file_name)
            || !path
                .extension()
                .is_some_and(|ext| ext.eq_ignore_ascii_case("xlsx"))
        {
            return Err(invalid("请选择合法的 .xlsx 输出文件。"));
        }
        crate::paths::atomic_write(&path, &bytes).map_err(unavailable)?;
        return serde_json::to_vec(&command(
            rows.len(),
            &path.to_string_lossy(),
            "审计日志已导出",
        ))
        .map_err(Into::into);
    }
    if body["confirmed"] != true {
        return Err(invalid("删除或清理审计记录前必须明确确认。"));
    }
    let mut criteria = Criteria::parse(body)?;
    let hard_limit = if operation == CLEANUP_AUDIT_LOGS {
        let days = body["daysToKeep"]
            .as_i64()
            .filter(|days| (1..=365_000).contains(days))
            .ok_or_else(|| invalid("保留天数必须为有效的正整数。"))?;
        criteria.end = Some(Utc::now() - chrono::Duration::days(days));
        200_000
    } else if operation == DELETE_AUDIT_LOGS_BY_CRITERIA && criteria.has_filter() {
        50_000
    } else {
        return Err(invalid("删除筛选结果前请至少设置一个筛选条件。"));
    };
    let limit = body["maxCount"]
        .as_u64()
        .filter(|value| *value > 0)
        .unwrap_or(hard_limit)
        .min(hard_limit) as usize;
    let count = service.store.transaction(|tx| {
        let (rows, _) = select(tx, &criteria, 0, limit, false)?;
        let ids: Vec<_> = rows.iter().filter_map(|row| row["id"].as_i64()).collect();
        crate::operation::check()?;
        let count = tx.delete_audits(&ids)?;
        if count > 0 {
            tx.append_audit_details(
                &AuditWrite {
                    kind: "audit-maintenance",
                    record_id: 0,
                    version: 1,
                    action: operation.id,
                    actor_id: actor.id,
                    occurred_at: &Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
                    note: "",
                },
                &json!({"newValues":{"affectedCount":count}}),
            )?;
        }
        Ok(count)
    })?;
    serde_json::to_vec(&command(count, "", "审计记录已清理")).map_err(Into::into)
}
