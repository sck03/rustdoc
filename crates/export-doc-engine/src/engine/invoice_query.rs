//! One authorized query feeds the page, export count and every exported row.
#[allow(unused_imports)]
use super::{NativeService, error::error};
use super::{
    auth,
    error::{Result, invalid, unavailable},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*};
use export_doc_domain::invoice_query::Criteria;
use serde_json::{Value, json};

pub fn select(store: &Store, actor: &Actor, body: &Value, action: &str) -> Result<Vec<Value>> {
    auth::authorize(actor, "document.query", action)?;
    let filter =
        serde_json::from_value(body.clone()).map_err(|e| invalid(format!("查询条件无效：{e}")))?;
    let criteria = Criteria::new(filter).map_err(invalid)?;
    let mut rows = vec![];
    for mut row in store.all("invoices")? {
        crate::operation::check()?;
        if !auth::visible(actor, "document.query", action, &row) {
            continue;
        }
        let invoice: ApiInvoiceDetailDto = serde_json::from_value(row.clone())
            .map_err(|_| unavailable("已保存的单据字段无效。"))?;
        if !criteria.matches(&invoice).map_err(unavailable)? {
            continue;
        }
        row["customerName"] = row["customerNameEN"].clone();
        row["exporterName"] = row["exporterNameEN"].clone();
        rows.push(contracts::dto(
            contracts::schema("ApiQueryInvoiceRowDto"),
            row,
        ));
    }
    rows.sort_by(|a, b| {
        b["invoiceDate"]
            .as_str()
            .cmp(&a["invoiceDate"].as_str())
            .then_with(|| b["id"].as_i64().cmp(&a["id"].as_i64()))
    });
    Ok(rows)
}
pub fn list(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let mut body = json!({});
    for (key, value) in query {
        if body.get(*key).is_some() {
            return Err(invalid("查询参数不能重复。"));
        }
        body[*key] = if matches!(*key, "customerId" | "exporterId") {
            if value.is_empty() {
                Value::Null
            } else {
                json!(
                    value
                        .parse::<i64>()
                        .map_err(|_| invalid("往来单位编号无效。"))?
                )
            }
        } else {
            json!(value)
        };
    }
    Ok(store::page_only(
        select(store, actor, &body, "view")?,
        query,
    ))
}
#[cfg(feature = "excel")]
pub const EXPORTS: &[Operation] = &[DOWNLOAD_QUERIED_INVOICES, SAVE_QUERIED_INVOICES_TO_PATH];
#[cfg(feature = "excel")]
pub fn export(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    use super::tasks::{TaskOutput, retry::Replay};
    let filter =
        serde_json::from_value(body.clone()).map_err(|e| invalid(format!("查询条件无效：{e}")))?;
    Criteria::new(filter).map_err(invalid)?;
    let destination = if operation == SAVE_QUERIED_INVOICES_TO_PATH {
        if service.provider()? != "SQLite" {
            return Err(error(403, "服务器不支持本机输出路径。"));
        }
        Some(super::excel::destination(body, true)?.ok_or_else(|| invalid("请选择输出文件。"))?)
    } else {
        None
    };
    let store = service.store.clone();
    let actor_id = actor.id;
    let request = body.clone();
    service.jobs.start_replayable(
        actor,
        "QueryInvoiceExcelExport",
        "导出查询结果 Excel",
        Some(Replay::new(operation, &[], body)),
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            let rows = select(&store, &actor, &request, "operate")?;
            if rows.len() > 50_000 {
                return Err(invalid("查询结果超过 50,000 条，请缩小筛选范围。"));
            }
            let bytes =
                export_doc_excel::table(export_doc_domain::invoice_query::COLUMNS, &rows, &|| {
                    crate::operation::check().map_err(|e| e.to_string())
                })
                .map_err(invalid)?;
            let current = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&current, operation, &[])?;
            for row in &rows {
                let source = store.get(
                    "invoices",
                    row["id"]
                        .as_i64()
                        .ok_or_else(|| unavailable("查询记录缺少编号。"))?,
                )?;
                if !auth::visible(&current, "document.query", "operate", &source) {
                    return Err(error(403, "导出期间数据权限发生变化，请重新查询。"));
                }
            }
            let mut output = TaskOutput::file(
                "查询结果.xlsx".into(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                bytes,
            );
            output.destination = destination;
            output.detail = format!("已导出 {} 条记录。", rows.len());
            Ok(output)
        },
    )
}
