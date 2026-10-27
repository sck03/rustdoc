//! Annual tariff imports use expiring, single-use previews in the business DB.
use super::{
    NativeService,
    error::{Result, conflict, invalid, unavailable},
    hs, import_previews, media,
    records::text,
    store::{self, Actor},
};
use crate::{contracts, generated_api::*, operation, paths};
use chrono::Datelike;
use export_doc_domain::{hs as rules, hs_import};
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    path::Path,
};
mod preview;

pub const UPLOADS: &[Operation] = &[PREVIEW_HS_CODES_IMPORT_UPLOAD, UPLOAD_HS_CODES_IMPORT_FILE];
pub const OPERATIONS: &[Operation] = &[
    PREVIEW_HS_CODES_IMPORT_UPLOAD,
    UPLOAD_HS_CODES_IMPORT_FILE,
    PREVIEW_HS_CODES_IMPORT_FROM_PATH,
    IMPORT_HS_CODES_FROM_PATH,
    COMMIT_HS_CODES_IMPORT,
];
pub const LOCAL: &[Operation] = &[PREVIEW_HS_CODES_IMPORT_FROM_PATH, IMPORT_HS_CODES_FROM_PATH];
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    metadata: &Value,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !paths::valid_file_name(name) {
        return Err(invalid("请选择合法的税则文件名。"));
    }
    let table = export_doc_excel::read_table_data(bytes, name, 50_000, &|| {
        crate::operation::check().map_err(|e| e.to_string())
    })
    .map_err(invalid)?;
    let (header, mapping) = hs_import::headers(&table.rows).map_err(invalid)?;
    let mode = match text(metadata, "mode").as_str() {
        "" | "Incremental" => "Incremental",
        "CompleteSnapshot" => "CompleteSnapshot",
        _ => return Err(invalid("税则导入模式无效。")),
    };
    let now = service.clock.now().map_err(unavailable)?;
    let year = metadata["effectiveYear"]
        .as_i64()
        .unwrap_or(i64::from(now.today.year()));
    if !(2000..=i64::from(now.today.year())).contains(&year) {
        return Err(invalid(
            "税则年度须为 2000 至本年度；未来年度请在生效后导入。",
        ));
    }
    let source =
        export_doc_domain::crm::text(&text(metadata, "sourceName"), "税则来源", 200, false)
            .map_err(invalid)?;
    let source = if source.is_empty() {
        Path::new(name)
            .file_stem()
            .and_then(|s| s.to_str())
            .unwrap_or("税则导入")
            .to_owned()
    } else {
        source
    };
    let existing = service
        .store
        .read_for(actor, operation, &[], |tx, _| hs::codes(tx))?;
    let items = preview::items(
        &preview::Input {
            table: &table,
            header,
            mapping: &mapping,
            source: &source,
            year,
            verified_at: &now.utc_now.to_rfc3339(),
            complete: mode == "CompleteSnapshot",
        },
        &existing,
    )?;
    drop(existing);
    let prepared = import_previews::prepare("hs-tariffs", &items)?;
    let token = service
        .store
        .transaction_for(actor, operation, &[], |tx, actor| {
            import_previews::save_prepared(tx, actor, &service.clock, &prepared)
        })?;
    drop(prepared);
    let counts = |kind: &str| items.iter().filter(|row| row["changeType"] == kind).count();
    let mut response = json!({"token":token,"fileName":name,"mode":mode,"sourceName":source,"effectiveYear":year,"worksheetName":table.sheet_name,"headerRowNumber":header+table.first_row,"confidence":100,
            "columns":mapping.iter().map(|(field,column)|json!({"field":field,"header":table.rows[header][*column],"columnNumber":column+1,"confidence":100})).collect::<Vec<_>>(),
            "addCount":counts("Add"),"updateCount":counts("Update"),"unchangedCount":counts("Unchanged"),"suspectedObsoleteCount":counts("SuspectedObsolete"),"conflictCount":counts("Conflict"),"invalidCount":counts("Invalid"),
            "warnings":if mode=="CompleteSnapshot"{vec!["完整年度库会标记缺失编码为疑似作废，请核对全部预检结果。"]}else{vec![]},"storagePolicy":"预检尚未写入税则；30 分钟内确认有效，只能提交一次。"});
    response["items"] = Value::Array(items);
    if operation == UPLOAD_HS_CODES_IMPORT_FILE {
        let committed = commit(service, actor, operation, &response)?;
        return Ok(
            json!({"success":true,"fileName":name,"totalCount":committed["addedCount"].as_i64().unwrap_or(0)+committed["updatedCount"].as_i64().unwrap_or(0),"message":committed["message"],"storagePolicy":"税则保存在当前业务数据库。"}),
        );
    }
    Ok(response)
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    if operation == COMMIT_HS_CODES_IMPORT {
        return commit(service, actor, operation, body);
    }
    if !LOCAL.contains(&operation) {
        return Err(invalid("此操作需要上传税则文件。"));
    }
    let path = std::path::PathBuf::from(text(body, "filePath"));
    let bytes = media::read_local(&path, export_doc_excel::MAX_INPUT)?;
    let name = path
        .file_name()
        .and_then(|n| n.to_str())
        .ok_or_else(|| invalid("税则文件名无效。"))?;
    upload(
        service,
        actor,
        if operation == IMPORT_HS_CODES_FROM_PATH {
            UPLOAD_HS_CODES_IMPORT_FILE
        } else {
            PREVIEW_HS_CODES_IMPORT_UPLOAD
        },
        body,
        name,
        &bytes,
    )
}
fn commit(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    service.store.transaction_for(actor, operation, &[], |tx, actor|{
        let items=import_previews::consume(tx,actor,&service.clock,"hs-tariffs",&text(body,"token"))?;
        let (mut added,mut updated,mut unchanged,mut obsolete,mut skipped)=(0,0,0,0,0);let mut written=BTreeSet::new();
        for row in items{
            operation::check()?;let change=text(&row,"changeType");let mut item=row["item"].clone();let code=text(&item,"code");
            if ["Invalid","Conflict"].contains(&change.as_str()){skipped+=1;continue;}
            if !written.insert(code.clone()){unchanged+=1;continue;}
            let current=tx.find_identity(hs::CODES,&code)?;
            if current.as_ref().map(|r|store::expected(r)).unwrap_or(0)!=store::expected(&item){return Err(conflict("税则在预检后已变化，请重新预检后提交。"));}
            if change=="Unchanged"{unchanged+=1;continue;}
            if change=="SuspectedObsolete"{item["status"]=json!("SuspectedObsolete");item["replacedByCodes"]=json!(row["replacementCandidates"].as_array().into_iter().flatten().filter_map(|v|v.as_str()).collect::<Vec<_>>().join(","));obsolete+=1;}
            else if current.is_none(){added+=1;}else{updated+=1;}
            hs::save_code(tx,actor,&item,true)?;
        }
        Ok(json!({"success":true,"addedCount":added,"updatedCount":updated,"unchangedCount":unchanged,"suspectedObsoleteCount":obsolete,"skippedCount":skipped,"message":format!("税则导入完成：新增 {added}，更新 {updated}，未变 {unchanged}，疑似作废 {obsolete}，跳过 {skipped}。") }))
    })
}
