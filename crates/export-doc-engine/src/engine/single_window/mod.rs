mod bridge;
#[cfg(feature = "excel")]
mod catalog_excel;
mod document_files;
mod documents;
mod imports;
mod packages;
mod profiles;
mod receipts;
mod references;
mod review;
mod tracking;
use super::{
    NativeService, auth,
    error::{Result, error, invalid, unavailable},
    records, settings,
    store::{self, Actor},
};
use crate::{contracts, generated_api::*};
use export_doc_domain::single_window::{self as rules, Business};
use export_doc_storage::Connection;
use serde_json::{Value, json};
pub const PERMISSION: &str = "document.single-window";
pub const OPERATIONS: &[Operation] = &[
    GET_CUSTOMS_COO_DOCUMENT,
    SAVE_CUSTOMS_COO_DOCUMENT,
    BUILD_CUSTOMS_COO_DEFAULTS,
    GET_CUSTOMS_COO_LOCKED_FIELDS,
    UNLOCK_CUSTOMS_COO_FIELDS,
    GET_AGENT_CONSIGNMENT_DOCUMENT,
    SAVE_AGENT_CONSIGNMENT_DOCUMENT,
    BUILD_AGENT_CONSIGNMENT_DEFAULTS,
    GET_AGENT_CONSIGNMENT_LOCKED_FIELDS,
    UNLOCK_AGENT_CONSIGNMENT_FIELDS,
    GET_SINGLE_WINDOW_REFERENCE_CATALOG,
    UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG,
    RESET_SINGLE_WINDOW_REFERENCE_CATALOG,
    IMPORT_SINGLE_WINDOW_REFERENCE_CATALOG_JSON,
    GET_CUSTOMS_COO_ISSUING_AUTHORITIES,
    GET_CUSTOMS_COO_EDITOR_OPTIONS,
    LIST_CUSTOMS_COO_PRODUCER_PROFILES,
    GET_CUSTOMS_COO_PRODUCER_PROFILE,
    CREATE_CUSTOMS_COO_PRODUCER_PROFILE,
    UPDATE_CUSTOMS_COO_PRODUCER_PROFILE,
    DELETE_CUSTOMS_COO_PRODUCER_PROFILE,
    GET_SINGLE_WINDOW_EXPORT_REVIEW,
    BUILD_CUSTOMS_COO_EXPORT_REVIEW,
    BUILD_AGENT_CONSIGNMENT_EXPORT_REVIEW,
    REPAIR_SINGLE_WINDOW_EXPORT_REVIEW_GROUPS,
    LIST_SINGLE_WINDOW_OPERATION_CENTER,
    GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL,
    GET_SINGLE_WINDOW_CLIENT_PROFILES,
    SAVE_SINGLE_WINDOW_CLIENT_PROFILE,
    ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE,
    DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
    DOWNLOAD_AGENT_CONSIGNMENT_SUBMIT_PACKAGE,
    SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
    SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
    DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
    SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,
    IMPORT_SINGLE_WINDOW_SUBMIT_PACKAGE,
    IMPORT_SINGLE_WINDOW_RECEIPT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
    DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
    COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS,
];
pub fn requires_local(operation: Operation) -> bool {
    packages::LOCAL.contains(&operation)
        || profiles::OPERATIONS.contains(&operation)
        || bridge::OPERATIONS.contains(&operation)
}
pub fn accepts_upload(operation: Operation) -> bool {
    #[cfg(feature = "excel")]
    if catalog_excel::UPLOADS.contains(&operation) {
        return true;
    }
    packages::UPLOADS.contains(&operation)
}
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    metadata: &Value,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    #[cfg(feature = "excel")]
    if catalog_excel::UPLOADS.contains(&operation) {
        return catalog_excel::preview(service, actor, operation, metadata, name, bytes);
    }
    imports::import(service, actor, operation, metadata, name, bytes)
}
pub const BATCHES: &str = "sw-batches";
pub fn parameter<'a>(values: &'a [(&str, String)], key: &str) -> &'a str {
    values
        .iter()
        .find(|(k, _)| *k == key)
        .map(|(_, v)| v.as_str())
        .unwrap_or("")
}
pub fn source(tx: &Connection, actor: &Actor, id: i64, action: &str) -> Result<Value> {
    let invoice = store::get(tx, "invoices", id)?;
    if !auth::visible(actor, PERMISSION, action, &invoice)
        || !auth::visible(actor, "document.invoices", "view", &invoice)
    {
        return Err(error(403, "没有此发票单一窗口资料的操作权限。"));
    }
    Ok(invoice)
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    if packages::OPERATIONS.contains(&operation) {
        return packages::handle(service, actor, operation, parameters, body);
    }
    if profiles::OPERATIONS.contains(&operation) {
        return serde_json::to_vec(&profiles::handle(
            service, actor, operation, parameters, body,
        )?)
        .map_err(Into::into);
    }
    if bridge::OPERATIONS.contains(&operation) {
        return serde_json::to_vec(&bridge::handle(service, actor, operation, body)?)
            .map_err(Into::into);
    }
    if references::OPERATIONS.contains(&operation) {
        return serde_json::to_vec(&references::handle(
            service, actor, operation, parameters, query, body,
        )?)
        .map_err(Into::into);
    }
    let value=service.store.transaction(|tx|{
        if operation==LIST_SINGLE_WINDOW_OPERATION_CENTER{
            return tracking::list(tx,actor,query);
        }
        if operation==GET_SINGLE_WINDOW_OPERATION_CENTER_DETAIL {
            let id=parameter(parameters,"batchId").parse().ok().filter(|n|*n>0).ok_or_else(||invalid("批次编号无效。"))?;
            return tracking::get(tx,actor,id,"view");
        }
        let business=if [GET_CUSTOMS_COO_DOCUMENT,SAVE_CUSTOMS_COO_DOCUMENT,BUILD_CUSTOMS_COO_DEFAULTS,GET_CUSTOMS_COO_LOCKED_FIELDS,UNLOCK_CUSTOMS_COO_FIELDS,BUILD_CUSTOMS_COO_EXPORT_REVIEW].contains(&operation){Business::Coo}
            else if [GET_AGENT_CONSIGNMENT_DOCUMENT,SAVE_AGENT_CONSIGNMENT_DOCUMENT,BUILD_AGENT_CONSIGNMENT_DEFAULTS,GET_AGENT_CONSIGNMENT_LOCKED_FIELDS,UNLOCK_AGENT_CONSIGNMENT_FIELDS,BUILD_AGENT_CONSIGNMENT_EXPORT_REVIEW].contains(&operation){Business::Acd}
            else {Business::parse(parameter(parameters,"businessType")).map_err(invalid)?};
        let id=parameter(parameters,"invoiceId").parse::<i64>().ok().filter(|n|*n>0).ok_or_else(||invalid("请选择有效来源发票。"))?;
        let mut loaded=documents::load(tx,actor,service,business,id,"view")?;
        match operation{
            GET_CUSTOMS_COO_DOCUMENT|GET_AGENT_CONSIGNMENT_DOCUMENT=>Ok(loaded.current),
            BUILD_CUSTOMS_COO_DEFAULTS|BUILD_AGENT_CONSIGNMENT_DEFAULTS=>Ok(loaded.defaults),
            GET_CUSTOMS_COO_LOCKED_FIELDS|GET_AGENT_CONSIGNMENT_LOCKED_FIELDS=>Ok(rules::draft::details(business,&loaded.current,&loaded.defaults,&loaded.locks)),
            SAVE_CUSTOMS_COO_DOCUMENT|SAVE_AGENT_CONSIGNMENT_DOCUMENT=>{
                source(tx,actor,id,"edit")?;
                let saved=documents::save(tx,actor,service,business,id,body,&loaded)?;Ok(json!({"success":true,"id":saved["id"],"document":saved,"message":"单证草稿已保存。"}))
            }
            UNLOCK_CUSTOMS_COO_FIELDS|UNLOCK_AGENT_CONSIGNMENT_FIELDS=>{
                source(tx,actor,id,"edit")?;
                let selected:Vec<String>=serde_json::from_value(body["fieldKeys"].clone()).map_err(|_|invalid("请选择需要解锁的字段。"))?;
                if selected.is_empty()||selected.len()>10_000{return Err(invalid("请选择有效数量的锁定字段。"));}
                let mut changed=0;for key in selected {if loaded.locks.remove(&key){changed+=1;}}
                if changed>0{
                    let mut updated=loaded.defaults.clone();rules::draft::restore_locked(business,&mut updated,&loaded.current,&loaded.locks);
                    loaded.current=documents::save(tx,actor,service,business,id,&updated,&loaded)?;
                }
                Ok(json!({"success":true,"changedCount":changed,"document":loaded.current,"lockedFields":rules::draft::details(business,&loaded.current,&loaded.defaults,&loaded.locks)["fields"],"message":format!("已恢复 {changed} 个字段的建议值。")}))
            }
            REPAIR_SINGLE_WINDOW_EXPORT_REVIEW_GROUPS=>{
                source(tx,actor,id,"edit")?;
                let groups:Vec<String>=serde_json::from_value(body["groupKeys"].clone()).map_err(|_|invalid("请选择修复分组。"))?;
                let changed=review::repair(business,&mut loaded,&groups)?;
                if changed>0{loaded.current=documents::save(tx,actor,service,business,id,&loaded.current,&loaded)?;}
                Ok(json!({"success":true,"repairedGroupCount":changed,"review":review::build(business,&loaded.current,&loaded.defaults),"message":"所选分组已按当前来源资料修复。"}))
            }
            GET_SINGLE_WINDOW_EXPORT_REVIEW|BUILD_CUSTOMS_COO_EXPORT_REVIEW|BUILD_AGENT_CONSIGNMENT_EXPORT_REVIEW=>Ok(review::build(business,&loaded.current,&loaded.defaults)),
            _=>Err(invalid("单一窗口操作不属于当前工作区。")),
        }
    })?;
    serde_json::to_vec(&contracts::dto(contracts::response(operation.id), value))
        .map_err(Into::into)
}
