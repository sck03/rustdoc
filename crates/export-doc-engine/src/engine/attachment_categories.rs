use super::{
    auth,
    error::{Result, conflict, error, invalid},
    records::{id, text},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*};
use export_doc_storage::AuditWrite;
use serde_json::{Value, json};
pub const OPERATIONS: &[Operation] = &[
    LIST_BUSINESS_ATTACHMENT_CATEGORIES,
    CREATE_BUSINESS_ATTACHMENT_CATEGORY,
    UPDATE_BUSINESS_ATTACHMENT_CATEGORY,
    DELETE_BUSINESS_ATTACHMENT_CATEGORY,
];
const KIND: &str = "attachment-categories";
pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    store.transaction(|tx| {
        let can_manage=actor.admin && auth::authorize(actor,"document.invoices","manage").is_ok();
        let attachments=store::all(tx,"attachments")?;
        let count=|id:&Value| attachments.iter().filter(|row|row["categoryId"]==*id).count();
        if operation==LIST_BUSINESS_ATTACHMENT_CATEGORIES {
            let company=if let Some((_,value))=query.iter().find(|(key,_)|*key=="invoiceId") {
                let invoice_id=value.parse::<i64>().ok().filter(|id|*id>0).ok_or_else(||invalid("发票编号无效。"))?;
                let invoice=store::get(tx,"invoices",invoice_id)?;
                if !auth::visible(actor,"document.invoices","view",&invoice){return Err(error(403,"无权查看该发票分类。"));}
                text(&invoice,"companyScope")
            } else {actor.company.clone()};
            let mut items:Vec<_>=store::all(tx,KIND)?.into_iter().filter(|row|row["companyScope"]==company).map(|mut row| {
                row["attachmentCount"]=json!(if can_manage {count(&row["id"])}else{0});
                contracts::project(contracts::schema("BusinessAttachmentCategoryRecord"),row)
            }).collect();
            items.sort_by_key(|row|row["id"].as_i64());
            return Ok(json!({"companyScope":company,"items":items,"canManage":can_manage&&!company.is_empty()}));
        }
        if !can_manage{return Err(error(403,"共享分类目录由系统管理员维护。"));}
        let record_id=if operation==CREATE_BUSINESS_ATTACHMENT_CATEGORY{0}else{id(parameters)?};
        let mut value=if record_id>0{store::get(tx,KIND,record_id)?}else{contracts::initial(contracts::schema("BusinessAttachmentCategoryRecord"))};
        if record_id>0{
            let expected=query.iter().find(|(key,_)|*key=="expectedVersion").and_then(|(_,value)|value.parse().ok()).unwrap_or_else(||store::expected(body));
            store::check_version(&value,expected)?;
        }
        if operation==DELETE_BUSINESS_ATTACHMENT_CATEGORY{
            if count(&value["id"])>0{return Err(conflict("该分类仍有资料使用（含停用资料），请先调整资料分类。"));}
            if !tx.delete(KIND,record_id,value["versionNumber"].as_i64().unwrap_or(0))?{return Err(conflict("分类版本已变化。"));}
            tx.append_audit_details(&AuditWrite{kind:KIND,record_id,version:value["versionNumber"].as_i64().unwrap_or(0),action:"delete",actor_id:actor.id,occurred_at:&store::timestamp(),note:""},&super::audit_values::changes(Some(&value),None))?;
            return Ok(json!({"success":true,"message":"分类已删除。"}));
        }
        let company=if record_id>0{text(&value,"companyScope")}else{text(body,"companyScope")};
        if record_id==0{
            if !store::all(tx,"companies")?.iter().any(|row|row["code"]==company&&row["isActive"]==true){return Err(invalid("所属公司不存在或已停用。"));}
            if store::all(tx,KIND)?.iter().filter(|row|row["companyScope"]==company).count()>=100{return Err(conflict("每家公司最多设置 100 个资料分类。"));}
        }
        value["name"]=json!(export_doc_domain::crm::text(&text(body,"name"),"分类名称",80,true).map_err(invalid)?);
        value["attachmentCount"]=json!(count(&value["id"]));
        let identity=serde_json::to_string(&[company.clone(),store::normalize(&text(&value,"name"))])?;
        let mut owner=actor.clone();owner.company=company;
        let value=store::save(tx,KIND,record_id,value,Some(identity),&owner,if record_id==0{"create"}else{"edit"})?;
        Ok(contracts::project(contracts::schema("BusinessAttachmentCategoryRecord"),value))
    })
}
