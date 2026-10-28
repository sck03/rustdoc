//! One authorized query feeds the page, export count and every exported row.
#[cfg(feature = "excel")]
use super::{
    NativeService,
    error::{error, unavailable},
};
use super::{
    auth,
    error::{Result, invalid},
    store::{self, Actor, Store},
};
use crate::contracts;
#[cfg(feature = "excel")]
use crate::generated_api::*;
use export_doc_domain::invoice_query::Criteria;
use serde_json::{Value, json};

#[cfg(feature = "excel")]
fn select(store: &Store, actor: &Actor, body: &Value, action: &str) -> Result<Vec<Value>> {
    auth::authorize(actor, "document.query", action)?;
    let filter =
        serde_json::from_value(body.clone()).map_err(|e| invalid(format!("查询条件无效：{e}")))?;
    let criteria = Criteria::new(filter).map_err(invalid)?;
    store.read(|tx| {
        let mut rows = vec![];
        loop {
            crate::operation::check()?;
            let (total, batch) = query(tx, actor, &criteria, action, rows.len() as i64, 200)?;
            if total > 50_000 {
                return Err(invalid("查询结果超过 50,000 条，请缩小筛选范围。"));
            }
            let empty = batch.is_empty();
            rows.extend(batch);
            if empty || rows.len() as i64 >= total {
                return Ok(rows);
            }
        }
    })
}
fn query(
    tx: &export_doc_storage::Connection,
    actor: &Actor,
    criteria: &Criteria,
    action: &str,
    offset: i64,
    limit: i64,
) -> Result<(i64, Vec<Value>)> {
    let schema = contracts::schema("ApiQueryInvoiceRowDto");
    let mut fields: Vec<_> = schema["properties"]
        .as_object()
        .into_iter()
        .flat_map(|p| p.keys())
        .map(|key| {
            let source = match key.as_str() {
                "customerName" => "customerNameEN",
                "exporterName" => "exporterNameEN",
                key => key,
            };
            (key.as_str(), source)
        })
        .collect();
    for key in ["ownerUserId", "companyScope", "departmentId"] {
        fields.push((key, key));
    }
    let filter = serde_json::to_value(criteria.filter())?;
    Ok(tx.query_business(&export_doc_storage::BusinessQuery {
        list: export_doc_storage::BusinessList::Invoices {
            filter: &filter,
            tokens: criteria.tokens(),
        },
        scope: auth::data_scope(actor, "document.query", action),
        fields: &fields,
        offset,
        limit,
    })?)
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
    auth::authorize(actor, "document.query", "view")?;
    let criteria = Criteria::new(
        serde_json::from_value(body).map_err(|e| invalid(format!("查询条件无效：{e}")))?,
    )
    .map_err(invalid)?;
    let (page, size, offset) = store::page_parameters(query);
    let (total, rows) = self::query(
        &*store.connection()?,
        actor,
        &criteria,
        "view",
        offset,
        size as i64,
    )?;
    Ok(contracts::page(
        rows.into_iter()
            .map(|row| contracts::dto(contracts::schema("ApiQueryInvoiceRowDto"), row))
            .collect(),
        total as usize,
        page,
        size,
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
        operation,
        "QueryInvoiceExcelExport",
        "导出查询结果 Excel",
        Some(Replay::new(operation, &[], body)),
        move |_| {
            let actor = auth::current_actor(&store, actor_id)?;
            auth::authorize_operation(&actor, operation, &[])?;
            let rows = select(&store, &actor, &request, "operate")?;
            let bytes =
                export_doc_excel::table(export_doc_domain::invoice_query::COLUMNS, &rows, &|| {
                    crate::operation::check().map_err(|e| e.to_string())
                })
                .map_err(invalid)?;
            let ids = rows
                .iter()
                .map(|row| {
                    row["id"]
                        .as_i64()
                        .ok_or_else(|| unavailable("查询记录缺少编号。"))
                })
                .collect::<Result<Vec<_>>>()?;
            let mut output = TaskOutput::file(
                "查询结果.xlsx".into(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                bytes,
            );
            output.destination = destination;
            output.detail = format!("已导出 {} 条记录。", rows.len());
            Ok(output.check_on_publish(publication_check(ids)))
        },
    )
}

#[cfg(feature = "excel")]
pub(in crate::engine) fn publication_check(ids: Vec<i64>) -> super::tasks::PublicationCheck {
    super::tasks::PublicationCheck::new(move |tx, current| {
        let mut visible = 0;
        for ids in ids.chunks(500) {
            crate::operation::check()?;
            visible += tx
                .query_business(&export_doc_storage::BusinessQuery {
                    list: export_doc_storage::BusinessList::InvoiceIds(ids),
                    scope: auth::data_scope(current, "document.query", "operate"),
                    fields: &[("id", "id")],
                    offset: 0,
                    limit: 1,
                })?
                .0;
        }
        if visible as usize != ids.len() {
            return Err(error(403, "导出期间数据权限发生变化，请重新查询。"));
        }
        Ok(())
    })
}
