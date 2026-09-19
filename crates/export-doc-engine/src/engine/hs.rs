//! Shared HS catalog and knowledge operations. Scope and trust are distinct:
//! the catalog is shared; access to private historical invoices is still scoped.
use super::{
    NativeService,
    error::{Result, conflict, error, invalid},
    hs_learning, hs_search,
    records::{id, text},
    store::{self, Actor},
};
use crate::{contracts, generated_api::*, operation};
use export_doc_domain::hs;
use export_doc_storage::{AuditWrite, Connection};
use serde_json::{Value, json};
use std::collections::{BTreeMap, BTreeSet};

pub const CODES: &str = "hs-codes";
pub const EXAMPLES: &str = "hs-examples";
pub const FEEDBACK: &str = "hs-feedback";
pub const REPLACEMENTS: &str = "hs-replacements";
pub const CANDIDATES: &str = "hs-candidates";
pub const OPERATIONS: &[Operation] = &[
    LIST_HS_CODES,
    GET_HS_CODE,
    GET_INVOICE_HS_CODE,
    CREATE_HS_CODE,
    UPDATE_HS_CODE,
    DELETE_HS_CODE,
    DELETE_HS_CODES_BATCH,
    CLEAR_ALL_HS_CODES,
    SEARCH_HS_CODE_KNOWLEDGE,
    SEARCH_INVOICE_HS_CODE_KNOWLEDGE,
    LIST_HS_CODE_KNOWLEDGE_EXAMPLES,
    SAVE_HS_CODE_KNOWLEDGE_EXAMPLE,
    DELETE_HS_CODE_KNOWLEDGE_EXAMPLE,
    DELETE_HS_CODE_KNOWLEDGE_EXAMPLES_BATCH,
    RECORD_HS_CODE_KNOWLEDGE_FEEDBACK,
    RECORD_INVOICE_HS_CODE_KNOWLEDGE_FEEDBACK,
    DISCOVER_HS_CODE_HISTORY_CANDIDATES,
    LIST_HS_CODE_REMOTE_CANDIDATES,
    REVIEW_HS_CODE_REMOTE_CANDIDATE,
    REVIEW_HS_CODE_REMOTE_CANDIDATES_BATCH,
    RESET_HS_CODE_REMOTE_CANDIDATES,
];
pub fn read<'a>(query: &'a [(&str, String)], key: &str) -> &'a str {
    query
        .iter()
        .find(|(k, _)| *k == key)
        .map(|(_, v)| v.as_str())
        .unwrap_or("")
}
pub fn codes(tx: &Connection) -> Result<BTreeMap<String, ApiHsCodeDto>> {
    store::all(tx, CODES)?
        .into_iter()
        .map(|row| {
            let dto: ApiHsCodeDto =
                serde_json::from_value(contracts::project(contracts::schema("ApiHsCodeDto"), row))?;
            Ok((hs::code(&dto.code).map_err(invalid)?, dto))
        })
        .collect()
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let value=service.store.transaction(|tx|match operation {
        LIST_HS_CODES=>{
            let filter=hs::text(read(query,"keyword"));
            if filter.chars().count()>500{return Err(invalid("检索条件不能超过 500 字。"));}
            let mut rows=store::all(tx,CODES)?;rows.retain(|r|filter.is_empty()||["code","name","elements","description"].iter().any(|key|hs::text(&text(r,key)).contains(&filter)));
            rows.sort_by_key(|r|text(r,"code"));Ok(store::page_only(rows,query))
        }
        GET_HS_CODE|GET_INVOICE_HS_CODE=>{
            let code=hs::code(read(parameters,"code")).map_err(invalid)?;
            tx.find_identity(CODES,&code)?.ok_or_else(||error(404,"HS 编码不存在。"))
        }
        CREATE_HS_CODE|UPDATE_HS_CODE=>{
            let mut body=body.clone();
            if operation==UPDATE_HS_CODE {
                let code=hs::code(read(parameters,"code")).map_err(invalid)?;
                if hs::code(&text(&body,"code")).map_err(invalid)?!=code{return Err(invalid("不能通过编辑更改 HS 编码。"));}
                let previous=tx.find_identity(CODES,&code)?.ok_or_else(||error(404,"HS 编码不存在。"))?;
                if body["id"].as_i64().is_some_and(|id|id>0&&Some(id)!=previous["id"].as_i64()){return Err(invalid("记录编号与编码不一致。"));}
                body["id"]=previous["id"].clone();
            }
            save_code(tx,actor,&body,false)
        }
        DELETE_HS_CODE|DELETE_HS_CODES_BATCH|CLEAR_ALL_HS_CODES=>{
            let ids=if operation==DELETE_HS_CODE{BTreeSet::from([id(parameters)?])}else if operation==CLEAR_ALL_HS_CODES {
                if text(body,"confirmation")!="CLEAR"{return Err(invalid("请输入 CLEAR 确认清空税则主档。"));}
                store::all(tx,CODES)?.iter().filter_map(|r|r["id"].as_i64()).collect()
            }else{ids(body)?};
            let count=delete(tx,actor,CODES,&ids)?;Ok(json!({"success":true,"message":format!("已删除 {count} 个编码。"),"deletedCount":count}))
        }
        SEARCH_HS_CODE_KNOWLEDGE|SEARCH_INVOICE_HS_CODE_KNOWLEDGE=>hs_search::search(tx,query),
        LIST_HS_CODE_KNOWLEDGE_EXAMPLES=>{
            let filter=hs::text(read(query,"keyword"));let mut rows=store::all(tx,EXAMPLES)?;
            rows.retain(|r|filter.is_empty()||["rawReportedHsCode","resolvedCurrentHsCode","productName","specification"].iter().any(|key|hs::text(&text(r,key)).contains(&filter)));
            rows.sort_by(|a,b|(a["isManuallyVerified"]!=true).cmp(&(b["isManuallyVerified"]!=true)).then_with(||b["updatedAt"].as_str().cmp(&a["updatedAt"].as_str())));
            Ok(store::page_only(rows,query))
        }
        SAVE_HS_CODE_KNOWLEDGE_EXAMPLE=>hs_learning::save_example(tx,actor,body),
        DELETE_HS_CODE_KNOWLEDGE_EXAMPLE|DELETE_HS_CODE_KNOWLEDGE_EXAMPLES_BATCH=>{
            let ids=if operation==DELETE_HS_CODE_KNOWLEDGE_EXAMPLE{BTreeSet::from([id(parameters)?])}else{ids(body)?};
            let count=delete(tx,actor,EXAMPLES,&ids)?;Ok(json!({"success":true,"message":format!("已删除 {count} 条案例。"),"deletedCount":count}))
        }
        RECORD_HS_CODE_KNOWLEDGE_FEEDBACK|RECORD_INVOICE_HS_CODE_KNOWLEDGE_FEEDBACK=>{
            hs_learning::feedback(tx,actor,body,service.clock.now().map_err(super::error::unavailable)?.today.year())?;
            Ok(json!({"success":true,"message":"反馈已记录。"}))
        }
        DISCOVER_HS_CODE_HISTORY_CANDIDATES=>hs_learning::history(tx,actor,query),
        _=>hs_learning::remote_review(tx,actor,operation,query,body),
    })?;
    Ok(contracts::project(contracts::response(operation.id), value))
}
pub fn save_code(tx: &Connection, actor: &Actor, body: &Value, managed: bool) -> Result<Value> {
    let mut dto: ApiHsCodeDto = serde_json::from_value(contracts::project(
        contracts::schema("ApiHsCodeDto"),
        body.clone(),
    ))
    .map_err(|e| invalid(e.to_string()))?;
    hs::normalize(&mut dto).map_err(invalid)?;
    for date in [&dto.last_verified_at, &dto.update_time, &dto.observed_at]
        .into_iter()
        .flatten()
    {
        chrono::DateTime::parse_from_rfc3339(date)
            .map_err(|_| invalid("HS 验证时间须为包含时区的有效时间。"))?;
    }
    let previous = tx.find_identity(CODES, &dto.code)?;
    if !managed
        && dto.status == "Active"
        && previous
            .as_ref()
            .is_none_or(|row| row["status"] != "Active")
    {
        return Err(invalid(
            "手工录入不能建立当前有效编码，请使用经过验证的年度税则导入。",
        ));
    }
    if !managed
        && dto.status == "ReferenceOnly"
        && previous
            .as_ref()
            .is_some_and(|row| row["status"] == "Active")
    {
        return Ok(previous.unwrap());
    }
    let record_id = previous
        .as_ref()
        .and_then(|r| r["id"].as_i64())
        .unwrap_or(0);
    if !managed && dto.id > 0 && dto.id != record_id {
        return Err(error(404, "HS 编码记录不存在。"));
    }
    let expected = if managed {
        store::expected(body)
    } else if dto.id > 0 {
        store::expected(body)
    } else {
        previous
            .as_ref()
            .and_then(|r| r["versionNumber"].as_i64())
            .unwrap_or(0)
    };
    dto.update_time = Some(store::timestamp());
    let mut value = serde_json::to_value(&dto)?;
    value["expectedVersion"] = json!(expected);
    store::save(
        tx,
        CODES,
        record_id,
        value,
        Some(dto.code),
        actor,
        if record_id == 0 { "create" } else { "edit" },
    )
}
pub fn ids(body: &Value) -> Result<BTreeSet<i64>> {
    let ids: Vec<i64> = serde_json::from_value(body["ids"].clone())
        .map_err(|_| invalid("请选择有效的记录编号。"))?;
    if ids.is_empty() || ids.len() > 5000 || ids.iter().any(|id| *id <= 0) {
        return Err(invalid("一次最多选择 5000 条有效记录。"));
    }
    Ok(ids.into_iter().collect())
}
pub fn delete(tx: &Connection, actor: &Actor, kind: &str, ids: &BTreeSet<i64>) -> Result<usize> {
    let mut count = 0;
    for id in ids {
        operation::check()?;
        let Some(row) = tx.get(kind, *id)? else {
            continue;
        };
        let version = row["versionNumber"].as_i64().unwrap_or(0);
        if !tx.delete(kind, *id, version)? {
            return Err(conflict("记录已修改，请重新读取。"));
        }
        tx.append_audit_details(
            &AuditWrite {
                kind,
                record_id: *id,
                version,
                action: "delete",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: "",
            },
            &super::audit_values::changes(Some(&row), None),
        )?;
        count += 1;
    }
    Ok(count)
}
use chrono::Datelike;
