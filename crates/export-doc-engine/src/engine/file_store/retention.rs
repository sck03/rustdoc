//! Explicit archival review. Active references and holds cannot be approved away.
use super::*;
use crate::{
    engine::{
        NativeService, auth,
        store::{Actor, Connection},
    },
    generated_api::*,
};
use export_doc_storage::AuditWrite;
use serde_json::{Value, json};
const PLAN: &str = "file-retention-review";
fn text<'a>(body: &'a Value, key: &str) -> &'a str {
    body[key].as_str().unwrap_or("")
}
fn project(tx: &Connection, mut row: Value) -> Result<Value> {
    let today = crate::engine::units::today(tx)?;
    let expiry = export_doc_domain::retention::expires_on(
        row["basisDate"].as_str(),
        row["days"].as_u64().unwrap_or(0) as u32,
    )
    .map_err(invalid)?;
    row["basisDate"] = json!(row["basisDate"].as_str().unwrap_or(""));
    row["held"] = json!(row["held"] == 1);
    row["expiresOn"] = json!(expiry.map(|v| v.to_string()).unwrap_or_default());
    row["eligible"] = json!(
        expiry.is_some_and(|date| date <= today)
            && row["held"] == false
            && row["activeReferences"] == 0
    );
    Ok(row)
}
pub(in crate::engine) fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    service.store.transaction_as(actor,|tx,actor|{
        if !actor.admin{return Err(crate::engine::error::error(403,"档案保留仅限系统管理员。"));}auth::authorize_operation(actor,operation,&[])?;
        if operation==QUERY_FILE_RETENTION{
            let mut rows=tx.retained_files(text(body,"after"),101)?.as_array().cloned().unwrap_or_default();let more=rows.len()>100;rows.truncate(100);let next=if more{rows.last().and_then(|r|r["id"].as_str()).unwrap_or("").to_owned()}else{String::new()};
            let rows=rows.into_iter().map(|r|project(tx,r)).collect::<Result<Vec<_>>>()?;let mut result=json!({"items":rows,"nextCursor":next});if let Some(plan)=tx.settings(PLAN)?{if plan["state"]=="Ready"{result["plan"]=plan["summary"].clone();}}return Ok(result);
        }
        let revision=body["expectedVersion"].as_i64().unwrap_or(-1);
        let reason=text(body,"reason").trim();
        let result=if operation==SAVE_RETENTION_POLICY{
            tx.save_retention_policy(text(body,"archiveClass"),body["days"].as_i64().unwrap_or(0),revision)?;json!({"message":"保留政策已保存。0 天表示长期保留；到期仍需独立复核。","count":0,"bytes":0})
        }else if operation==CLASSIFY_RETAINED_FILE{
            let basis=text(body,"basisDate");let date=chrono::NaiveDate::parse_from_str(basis,"%Y-%m-%d").map_err(|_|invalid("请填写有效的归档或结案日期。"))?;if basis.len()!=10||date>crate::engine::units::today(tx)?{return Err(invalid("归档依据须为已发生的业务日期。"));}
            tx.classify_retained_file(text(body,"id"),text(body,"archiveClass"),basis,revision)?;json!({"message":"档案类别和依据日期已登记。","count":1,"bytes":0})
        }else if operation==HOLD_RETAINED_FILE{
            tx.hold_retained_file(text(body,"digest"),body["active"]==true,reason,revision)?;json!({"message":"暂停保留状态已更新。","count":1,"bytes":0})
        }else if operation==PREVIEW_RETENTION_DELETION{
            let ids=body["ids"].as_array().ok_or_else(||invalid("请选择待处置档案。"))?;let mut rows=Vec::new();let mut bytes=0u64;
            for id in ids{let row=tx.retained_file(id.as_str().unwrap_or(""))?.ok_or_else(||invalid("所选档案已不存在，请刷新。"))?;let row=project(tx,row)?;if row["eligible"]!=true{return Err(invalid(format!("档案“{}”仍被引用、暂停保留、长期保留或未到期。请核对类别、依据日期和保留政策。",text(&row,"fileName"))));}bytes+=row["size"].as_u64().unwrap_or(0);rows.push(row);}
            let summary=json!({"token":paths::nonce().map_err(unavailable)?,"count":rows.len(),"bytes":bytes,"expiresAt":(chrono::Utc::now()+chrono::Duration::minutes(15)).to_rfc3339(),"requestedBy":actor.id});
            tx.set_settings(PLAN,1,&json!({"state":"Ready","summary":summary,"rows":rows,"reason":reason}))?;summary
        }else if operation==APPROVE_RETENTION_DELETION{
            let mut plan=tx.settings(PLAN)?.ok_or_else(||invalid("没有有效待批准处置计划。"))?;if plan["summary"]["token"]!=body["token"]{return Err(conflict("处置计划已变更，请重新预览。"));}if plan["state"]=="Completed"{return Ok(json!({"message":"该处置计划已完成。","count":plan["summary"]["count"],"bytes":plan["summary"]["bytes"]}));}
            if plan["summary"]["requestedBy"]==actor.id{return Err(crate::engine::error::error(403,"正式档案处置需要另一名系统管理员复核批准，申请人不能自行批准。"));}
            let expires=chrono::DateTime::parse_from_rfc3339(text(&plan["summary"],"expiresAt")).map_err(|_|invalid("处置计划期限无效。"))?;if expires<chrono::Utc::now(){return Err(conflict("处置预检已过期，请重新预览。"));}
            for expected in plan["rows"].as_array().ok_or_else(||invalid("处置计划损坏。"))?{let current=tx.retained_file(text(expected,"id"))?.ok_or_else(||conflict("档案已变化，请重新预检。"))?;let current=project(tx,current)?;if &current!=expected||current["eligible"]!=true{return Err(conflict("档案引用、暂停保留或政策已变化，未执行处置，请重新预览。"));}tx.remove_retained_file(text(&current,"id"),current["revision"].as_i64().unwrap_or(0))?;}
            plan["state"]=json!("Completed");plan["approvedBy"]=json!(actor.id);tx.set_settings(PLAN,2,&plan)?;
            json!({"message":"已移除获批的归档引用；物理对象须再经无引用回收。已有恢复点按自身保留期继续保护历史资料。","count":plan["summary"]["count"],"bytes":plan["summary"]["bytes"]})
        }else{return Err(invalid("档案保留操作无效。"));};
        tx.append_audit_details(&AuditWrite{kind:"file-retention",record_id:0,version:revision.max(0)+1,action:operation.id,actor_id:actor.id,occurred_at:&crate::engine::store::timestamp(),note:reason},&json!({"request":body,"result":result}))?;Ok(result)
    })
}

pub(in crate::engine) fn download_info(
    store: &crate::engine::store::Store,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<export_doc_storage::FileInfo> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        if !actor.admin {
            return Err(crate::engine::error::error(403, "档案取回仅限系统管理员。"));
        }
        let id = crate::engine::request_values::find(parameters, "id").unwrap_or("");
        let row = tx.retained_file(id)?.ok_or_else(|| {
            crate::engine::error::error(404, "归档引用已不存在，请刷新档案列表。")
        })?;
        Ok(export_doc_storage::FileInfo {
            file_name: text(&row, "fileName").into(),
            media_type: "application/octet-stream".into(),
            object: export_doc_storage::FileObject {
                digest: text(&row, "digest").into(),
                archive_identity: text(&row, "archiveIdentity").into(),
                size: row["size"]
                    .as_u64()
                    .ok_or_else(|| invalid("档案容量记录无效。"))?,
            },
        })
    })
}
