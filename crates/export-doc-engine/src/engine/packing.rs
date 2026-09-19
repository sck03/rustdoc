//! Container plans, global container types and bounded native analysis/PDF.
use super::{
    NativeService, audit_values, auth,
    error::{Result, conflict, error, invalid, unavailable},
    records::{id, text},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*, operation, paths};
use export_doc_domain::packing;
use export_doc_storage::{AuditWrite, Connection};
use serde_json::{Value, json};
use std::{
    path::Path,
    sync::TryLockError,
    time::{Duration, Instant},
};

pub const OPERATIONS: &[Operation] = &[
    ANALYZE_CONTAINER_PACKING,
    LIST_CONTAINER_PACKING_PROJECTS,
    GET_CONTAINER_PACKING_PROJECT,
    SAVE_CONTAINER_PACKING_PROJECT,
    DELETE_CONTAINER_PACKING_PROJECT,
    LIST_CONTAINER_PACKING_CONTAINER_TYPES,
    SAVE_CONTAINER_PACKING_CONTAINER_TYPE,
    DELETE_CONTAINER_PACKING_CONTAINER_TYPE,
    DOWNLOAD_CONTAINER_PACKING_PDF,
    SAVE_CONTAINER_PACKING_PDF_TO_PATH,
];
const PERMISSION: &str = "document.container-packing";
const PROJECTS: &str = "container-projects";
const TYPES: &str = "container-types";
const POLICY: &str = "方案保存在当前业务数据库；分析和预览不写入方案，PDF 保存到选择的位置。";

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    if matches!(
        operation,
        DOWNLOAD_CONTAINER_PACKING_PDF | SAVE_CONTAINER_PACKING_PDF_TO_PATH
    ) {
        let request: ApiContainerPackingPdfRequest =
            serde_json::from_value(body.clone()).map_err(|e| invalid(e.to_string()))?;
        let document = export_doc_report::packing::document(&request)?;
        let bytes = export_doc_report::pdf_document(
            &document,
            &service.paths.font_path,
            &operation::cancellation_flag(),
        )?;
        operation::check()?;
        if operation == DOWNLOAD_CONTAINER_PACKING_PDF {
            return Ok(bytes);
        }
        let path = Path::new(request.destination_path.as_deref().unwrap_or(""));
        paths::save_pdf(path, &bytes).map_err(unavailable)?;
        return Ok(serde_json::to_vec(
            &json!({"success":true,"filePath":path,"sizeBytes":bytes.len(),"message":"装柜现场 PDF 已保存。"}),
        )?);
    }
    let value = if operation == ANALYZE_CONTAINER_PACKING {
        let dto: ApiContainerPackingAnalyzeRequest =
            serde_json::from_value(body.clone()).map_err(|e| invalid(e.to_string()))?;
        let request = packing::Request::new(&dto).map_err(invalid)?;
        let _lease = service
            .packing_gate
            .try_lock()
            .map_err(|cause| match cause {
                TryLockError::WouldBlock => error(429, "装柜分析正在进行，请稍后重试。"),
                TryLockError::Poisoned(_) => unavailable("装柜分析状态异常。"),
            })?;
        let start = Instant::now();
        let analysis = packing::analyze(&request, || {
            operation::check()?;
            if start.elapsed() > Duration::from_secs(25) {
                Err(error(504, "装柜分析超过时限，请使用托盘约束或拆分方案。"))
            } else {
                Ok(())
            }
        })?;
        json!({"analysis":analysis,"storagePolicy":POLICY})
    } else {
        service.store.transaction(|tx| match operation {
            LIST_CONTAINER_PACKING_PROJECTS => {
                let limit = query.iter().find(|(key, _)| *key == "limit").and_then(|(_, value)| value.parse::<usize>().ok()).unwrap_or(30).clamp(1, 200);
                let mut rows: Vec<_> = store::all(tx, PROJECTS)?.into_iter().filter(|r| auth::visible(actor, PERMISSION, "view", r)).collect();
                rows.sort_by(|a, b| b["updatedAt"].as_str().cmp(&a["updatedAt"].as_str()).then_with(|| b["id"].as_i64().cmp(&a["id"].as_i64())));
                rows.truncate(limit);
                Ok(json!({"projects":rows,"storagePolicy":POLICY}))
            }
            GET_CONTAINER_PACKING_PROJECT => Ok(json!({"project":project(tx, actor, id(parameters)?, "view")?,"storagePolicy":POLICY})),
            SAVE_CONTAINER_PACKING_PROJECT => save_project(tx, actor, body),
            DELETE_CONTAINER_PACKING_PROJECT => {
                let row = project(tx, actor, id(parameters)?, "delete")?;
                remove(tx, actor, PROJECTS, &row)?;
                Ok(json!({"success":true,"message":"方案已删除。"}))
            }
            LIST_CONTAINER_PACKING_CONTAINER_TYPES => {
                let mut rows = store::all(tx, TYPES)?;
                rows.sort_by_key(|row| (row["isSystemDefault"] != true, store::normalize(&text(row, "name"))));
                Ok(json!({"containerTypes":rows,"storagePolicy":POLICY}))
            }
            SAVE_CONTAINER_PACKING_CONTAINER_TYPE => save_type(tx, actor, body),
            DELETE_CONTAINER_PACKING_CONTAINER_TYPE => {
                let type_id = id(parameters)?;
                let row = store::get(tx, TYPES, type_id)?;
                if row["isSystemDefault"] == true { return Err(conflict("系统默认柜型不能删除。")); }
                remove(tx, actor, TYPES, &row)?;
                Ok(json!({"success":true,"message":"自定义柜型已删除。"}))
            }
            _ => Err(invalid("装柜操作无效。")),
        })?
    };
    Ok(serde_json::to_vec(&contracts::project(
        contracts::response(operation.id),
        value,
    ))?)
}
fn project(tx: &Connection, actor: &Actor, record_id: i64, action: &str) -> Result<Value> {
    let row = store::get(tx, PROJECTS, record_id)?;
    if !auth::visible(actor, PERMISSION, action, &row) {
        return Err(error(403, "方案不在当前账号可操作的数据范围内。"));
    }
    Ok(row)
}
fn save_project(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    let mut value = contracts::project(
        contracts::request(SAVE_CONTAINER_PACKING_PROJECT.id),
        body.clone(),
    );
    let record_id = value["id"].as_i64().unwrap_or(0);
    if record_id < 0 {
        return Err(invalid("方案编号无效。"));
    }
    if record_id > 0 {
        project(tx, actor, record_id, "edit")?;
    }
    value["name"] = json!(
        export_doc_domain::crm::text(&text(&value, "name"), "方案名称", 200, true)
            .map_err(invalid)?
    );
    value["containerType"] = json!(
        export_doc_domain::crm::text(&text(&value, "containerType"), "柜型名称", 100, false)
            .map_err(invalid)?
    );
    let request: ApiContainerPackingAnalyzeRequest =
        serde_json::from_value(value.clone()).map_err(|e| invalid(e.to_string()))?;
    packing::Request::new(&request).map_err(invalid)?;
    let saved = store::save(
        tx,
        PROJECTS,
        record_id,
        value,
        None,
        actor,
        if record_id == 0 { "create" } else { "edit" },
    )?;
    Ok(
        json!({"success":true,"id":saved["id"],"project":saved,"message":"装柜方案已保存。","storagePolicy":POLICY}),
    )
}
fn builtins() -> Vec<Value> {
    [("20GP",589,235,239,28,21000),("40GP",1203,235,239,58,26000),("40HQ",1203,235,269,68,26000)]
        .into_iter().map(|(name,length,width,height,volume,weight)|
            json!({"name":name,"length":length,"width":width,"height":height,"maxVolume":volume,"maxWeight":weight,"isSystemDefault":true})).collect()
}
pub fn seed(store: &Store) -> Result<()> {
    store.transaction(|tx| {
        let existing = store::all(tx, TYPES)?;
        let actor = Actor {
            id: 0,
            name: "系统".into(),
            company: String::new(),
            department: String::new(),
            admin: true,
            grants: vec![],
        };
        for row in builtins() {
            let name = text(&row, "name");
            if !existing
                .iter()
                .any(|row| store::normalize(&text(row, "name")) == store::normalize(&name))
            {
                store::save(tx, TYPES, 0, row, Some(name), &actor, "create")?;
            }
        }
        Ok(())
    })
}
fn save_type(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    let name = export_doc_domain::crm::text(&text(body, "name"), "柜型名称", 100, true)
        .map_err(invalid)?;
    let mut record_id = body["id"].as_i64().unwrap_or(0);
    if record_id < 0
        || builtins()
            .iter()
            .any(|row| store::normalize(&text(row, "name")) == store::normalize(&name))
    {
        return Err(conflict("系统默认柜型不支持覆盖，请换一个名称保存。"));
    }
    let mut value = contracts::project(
        contracts::request(SAVE_CONTAINER_PACKING_CONTAINER_TYPE.id),
        body.clone(),
    );
    value["name"] = json!(name);
    let dimensions: ApiContainerDimensionsDto = serde_json::from_value(json!({"length":value["length"],"width":value["width"],"height":value["height"],"volume":value["maxVolume"],"maxWeight":value["maxWeight"]})).map_err(|e| invalid(e.to_string()))?;
    packing::Dimensions::new(&dimensions).map_err(invalid)?;
    if record_id == 0 {
        if let Some(existing) = store::all(tx, TYPES)?
            .into_iter()
            .find(|row| store::normalize(&text(row, "name")) == store::normalize(&name))
        {
            record_id = existing["id"].as_i64().unwrap_or(0);
        }
    }
    if record_id > 0 {
        let previous = store::get(tx, TYPES, record_id)?;
        if previous["isSystemDefault"] == true {
            return Err(conflict("系统默认柜型不支持覆盖。"));
        }
        value["expectedVersion"] = previous["versionNumber"].clone();
    }
    value["isSystemDefault"] = json!(false);
    let saved = store::save(
        tx,
        TYPES,
        record_id,
        value,
        Some(name),
        actor,
        if record_id == 0 { "create" } else { "edit" },
    )?;
    Ok(
        json!({"success":true,"id":saved["id"],"containerType":saved,"message":"柜型已保存。","storagePolicy":POLICY}),
    )
}
fn remove(tx: &Connection, actor: &Actor, kind: &str, row: &Value) -> Result<()> {
    let record_id = row["id"].as_i64().unwrap_or(0);
    let version = row["versionNumber"].as_i64().unwrap_or(0);
    if !tx.delete(kind, record_id, version)? {
        return Err(conflict("记录版本已变化。"));
    }
    tx.append_audit_details(
        &AuditWrite {
            kind,
            record_id,
            version,
            action: "delete",
            actor_id: actor.id,
            occurred_at: &store::timestamp(),
            note: "",
        },
        &audit_values::changes(Some(row), None),
    )?;
    Ok(())
}
