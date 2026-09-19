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
    let year = metadata["effectiveYear"]
        .as_i64()
        .unwrap_or(service.clock.now().map_err(unavailable)?.today.year() as i64);
    if !(2000..=2100).contains(&year) {
        return Err(invalid("税则年度須为 2000 至 2100。"));
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
    let response=service.store.transaction(|tx|{
        let existing=hs::codes(tx)?;let mut items=vec![];let mut seen=BTreeMap::<String,Value>::new();let mut conflicts=BTreeSet::new();
        for (index,row) in table.rows.iter().enumerate().skip(header+1){
            operation::check()?;if row.iter().all(|v|v.trim().is_empty()){continue;}
            let mut item=contracts::initial(contracts::schema("ApiHsCodeDto"));
            for (field,column) in &mapping{item[*field]=json!(row.get(*column).map(String::as_str).unwrap_or(""));}
            let first=hs_import::unit(&text(&item,"unit"));let second=hs_import::unit(&text(&item,"unit2"));
            item["unit"]=json!(if second.is_empty()||first==second{first}else{format!("{first}/{second}")});
            item["status"]=json!("Active");item["sourceName"]=json!(source);item["effectiveYear"]=json!(year);item["lastVerifiedAt"]=json!(store::timestamp());
            let parsed:Result<ApiHsCodeDto>=serde_json::from_value(item.clone()).map_err(|e|invalid(e.to_string())).and_then(|mut dto|{rules::normalize(&mut dto).map_err(invalid)?;Ok(dto)});
            let mut change="Add";let mut message=String::new();let mut changed=vec![];
            match parsed {
                Err(cause)=>{change="Invalid";message=cause.message;},
                Ok(dto)=>{
                    item=serde_json::to_value(&dto)?;let code=dto.code.clone();
                    if let Some(prior)=seen.get(&code){if comparable(prior)!=comparable(&item){conflicts.insert(code.clone());}else{change="Unchanged";}}
                    seen.insert(code.clone(),item.clone());
                    if let Some(old)=existing.get(&code){let old=serde_json::to_value(old)?;item["id"]=old["id"].clone();item["rowVersion"]=old["rowVersion"].clone();
                        changed=comparable(&item).as_object().unwrap().iter().filter(|(key,value)|old[*key]!=**value).map(|(key,_)|key.clone()).collect();
                        if change!="Unchanged"{change=if changed.is_empty(){"Unchanged"}else{"Update"};}
                    }
                }
            }
            items.push(json!({"changeType":change,"rowNumber":index+table.first_row,"item":item,"changedFields":changed,"replacementCandidates":[],"message":message}));
        }
        if items.is_empty(){return Err(invalid("税则文件没有可读取的明细。"));}
        for item in &mut items{if conflicts.contains(&text(&item["item"],"code")){item["changeType"]=json!("Conflict");item["message"]=json!("同一编码在文件中存在不同内容，不会写入。" );}}
        if mode=="CompleteSnapshot"{
            for (code,old) in &existing{if seen.contains_key(code){continue;}operation::check()?;
                let replacements:Vec<_>=seen.iter().filter(|(_,item)|rules::text(&text(item,"name"))==rules::text(&old.name)).map(|(code,_)|code.clone()).take(20).collect();
                items.push(json!({"changeType":"SuspectedObsolete","rowNumber":0,"item":old,"changedFields":["status"],"replacementCandidates":replacements,"message":"完整年度文件未出现，确认后标记为疑似作废，保留原编码。"}));
            }
        }
        let counts=|kind:&str|items.iter().filter(|row|row["changeType"]==kind).count();
        let token=import_previews::save(tx,actor,&service.clock,"hs-tariffs",&items)?;
        Ok(json!({"token":token,"fileName":name,"mode":mode,"sourceName":source,"effectiveYear":year,"worksheetName":table.sheet_name,"headerRowNumber":header+table.first_row,"confidence":100,
            "columns":mapping.iter().map(|(field,column)|json!({"field":field,"header":table.rows[header][*column],"columnNumber":column+1,"confidence":100})).collect::<Vec<_>>(),
            "addCount":counts("Add"),"updateCount":counts("Update"),"unchangedCount":counts("Unchanged"),"suspectedObsoleteCount":counts("SuspectedObsolete"),"conflictCount":counts("Conflict"),"invalidCount":counts("Invalid"),
            "warnings":if mode=="CompleteSnapshot"{vec!["完整年度库会标记缺失编码为疑似作废，请核对全部预检结果。"]}else{vec![]},"items":items,"storagePolicy":"预检尚未写入税则；30 分钟内确认有效，只能提交一次。"}))
    })?;
    if operation == UPLOAD_HS_CODES_IMPORT_FILE {
        let committed = commit(service, actor, &response)?;
        return Ok(
            json!({"success":true,"fileName":name,"totalCount":committed["addedCount"].as_i64().unwrap_or(0)+committed["updatedCount"].as_i64().unwrap_or(0),"message":committed["message"],"storagePolicy":"税则保存在当前业务数据库。"}),
        );
    }
    Ok(response)
}
fn comparable(item: &Value) -> Value {
    let keys = hs_import::COLUMNS
        .iter()
        .map(|(k, _)| *k)
        .filter(|key| *key != "unit2")
        .chain(["status", "sourceName", "effectiveYear"]);
    Value::Object(
        keys.map(|key| (key.to_string(), item[key].clone()))
            .collect(),
    )
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    if operation == COMMIT_HS_CODES_IMPORT {
        return commit(service, actor, body);
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
fn commit(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    service.store.transaction(|tx|{
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
