use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    records::{required, text},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*, paths};
use export_doc_storage::AuditWrite;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub const FILE_LIMIT: usize = super::file_store::policy::MAX_UPLOAD_BYTES as usize;
pub const OPERATIONS: &[Operation] = &[
    LIST_BUSINESS_ATTACHMENTS,
    GET_BUSINESS_ATTACHMENT,
    UPLOAD_BUSINESS_ATTACHMENT,
    UPDATE_BUSINESS_ATTACHMENT,
    EDIT_BUSINESS_ATTACHMENT_METADATA,
    DELETE_BUSINESS_ATTACHMENT,
    DOWNLOAD_BUSINESS_ATTACHMENT,
    SAVE_BUSINESS_ATTACHMENT_TO_PATH,
];

fn invoice_in(
    tx: &crate::engine::store::Connection,
    actor: &Actor,
    id: i64,
    action: &str,
) -> Result<Value> {
    auth::authorize(actor, "document.invoices", action)?;
    let invoice = store::get(tx, "invoices", id)?;
    if !auth::visible(actor, "document.invoices", action, &invoice) {
        return Err(error(403, "没有访问此发票资料的权限。"));
    }
    Ok(invoice)
}
pub fn upload(
    store: &Store,
    actor: &Actor,
    invoice_id: i64,
    metadata: Value,
    file_name: &str,
    input: &mut (impl std::io::BufRead + std::io::Seek),
    size: u64,
) -> Result<Value> {
    required(&metadata, "title", "资料名称", 200)?;
    required(&metadata, "uploadKey", "上传编号", 100)?;
    let attachment_id = metadata["attachmentId"].as_i64().unwrap_or(0);
    let category_id = metadata["categoryId"].as_i64().unwrap_or(0);
    store.read(|tx| invoice_in(tx, actor, invoice_id, "operate"))?;
    let (name, content_type, prepared) =
        super::document_upload::prepare(store, file_name, input, size, false)?;
    let hash = prepared.object.digest.as_str();
    store.transaction_as(actor, |transaction, actor|{
        let invoice = invoice_in(transaction, actor, invoice_id, "operate")?;
        let category = store::get(transaction,"attachment-categories",category_id)?;
        if category["companyScope"] != invoice["companyScope"] { return Err(error(403,"资料分类不属于发票所在公司。")); }
        let existing=transaction.query_generic(&export_doc_storage::GenericQuery {kind:"attachments",scope:export_doc_storage::DataScope {all:true,..Default::default()},filters:&[("lastUploadKey",text(&metadata,"uploadKey"))],keyword:"",search_root:None,fields:&[],offset:0,limit:1})?.1.into_iter().next();
        if let Some(existing)=existing{
            if existing["invoiceId"]==invoice_id&&existing["lastDigest"]==hash{return Ok(existing.clone());}
            return Err(conflict("同一上传编号已用于不同内容。"));
        }
        store.files.configuration()?.policy.check_upload(size)?;
        let mut record=if attachment_id>0{
            let previous=store::get(transaction,"attachments",attachment_id)?;
            if previous["invoiceId"]!=invoice_id{return Err(invalid("不能改变资料所属发票。"));}
            if previous["isArchived"]==true{return Err(conflict("请先恢复资料，再上传新版本。"));}
            if previous["categoryId"]!=category_id{return Err(conflict("上传新版本不能修改资料分类，请先修正资料信息。"));}
            for key in ["title","poNumber","styleNo"] { if text(&previous,key)!=text(&metadata,key) {return Err(conflict("上传新版本不能修改资料信息，请先使用修正资料信息。"));} }
            store::check_version(&previous,store::expected(&metadata))?;previous
        }else{contracts::initial(contracts::schema("BusinessAttachmentRecord"))};
        let revision=record["latestRevision"].as_i64().unwrap_or(0)+1;
        record["invoiceId"]=json!(invoice_id);record["invoiceNo"]=invoice["invoiceNo"].clone();record["invoiceType"]=invoice["type"].clone();record["customerName"]=invoice["customerNameEN"].clone();
        for key in ["title","poNumber","styleNo"]{record[key]=json!(text(&metadata,key));}record["categoryId"]=json!(category_id);
        record["categoryName"]=category["name"].clone();record["latestRevision"]=json!(revision);record["lastUploadKey"]=metadata["uploadKey"].clone();record["lastDigest"]=json!(hash);record["canEdit"]=json!(true);record["canDelete"]=json!(actor.admin);
        let revision_record=json!({"revision":revision,"fileName":name,"contentType":content_type,"length":size,"sha256":hash,"uploadedBy":actor.name,"note":text(&metadata,"note"),"createdAt":store::timestamp()});
        record.as_object_mut().unwrap().remove("revisions");
        let record=store::save_in_scope(transaction,"attachments",attachment_id,record,None,actor,"upload",Some(&invoice))?;
        transaction.publish_file(&prepared,&format!("attachment:{revision}"),record["id"].as_i64().unwrap_or(0),&name,content_type)?;
        transaction.append_attachment_history(record["id"].as_i64().unwrap(),"revision",&revision_record)?;
        Ok(record)
    })
}
pub fn read(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    id: i64,
    revision: i64,
) -> Result<(Value, Vec<u8>)> {
    let file = download_info(store, actor, operation, id, revision)?;
    let output = store.files.load(&file, FILE_LIMIT)?;
    if download_info(store, actor, operation, id, revision)? != file {
        return Err(conflict("资料版本已变化，请重新读取。"));
    }
    Ok((
        json!({"fileName":output.file_name,"contentType":output.media_type}),
        output.content,
    ))
}
pub fn download_info(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    id: i64,
    revision: i64,
) -> Result<export_doc_storage::FileInfo> {
    let file = store.read_for(actor, operation, &[], |tx, actor| {
        let record = store::get(tx, "attachments", id)?;
        invoice_in(tx, actor, parent_invoice_id(&record)?, "view")?;
        let kind = format!("attachment:{revision}");
        let info = tx
            .blob_info(id, &kind)?
            .ok_or_else(|| error(404, "资料版本不存在。"))?;
        if info.size == 0 || info.size > FILE_LIMIT as u64 {
            return Err(unavailable("资料内容容量无效，已停止下载。"));
        }
        let file = tx
            .file_info(id, &kind)?
            .ok_or_else(|| unavailable("资料内容缺失，已停止下载。"))?;
        if file.object.size != info.size {
            return Err(unavailable("资料内容长度不一致，已停止下载。"));
        }
        Ok(file)
    })?;
    Ok(file)
}
pub fn handle(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: Value,
) -> Result<Vec<u8>> {
    let value = if operation == LIST_BUSINESS_ATTACHMENTS {
        let invoice_id = query
            .iter()
            .find(|(key, _)| *key == "invoiceId")
            .and_then(|(_, value)| value.parse::<i64>().ok());
        let archived = query
            .iter()
            .any(|(key, value)| *key == "includeArchived" && value == "true");
        let category_id = query
            .iter()
            .find(|(key, _)| *key == "categoryId")
            .and_then(|(_, value)| value.parse::<i64>().ok());
        store.read_for(actor, operation, query, |tx, actor| {
            let (page, size, offset) = store::page_parameters(query);
            let keyword = store::normalize(super::hs::read(query,"keyword"));
            let (total, mut items) = tx.query_business(&export_doc_storage::BusinessQuery {
                list: export_doc_storage::BusinessList::Attachments { invoice_id, category_id, archived, keyword: &keyword, status: super::hs::read(query,"status") },
                scope: auth::data_scope(actor,"document.invoices","view"),
                fields: &[], offset, limit: size as i64,
            })?;
            let mut invoice_ids = items.iter().map(parent_invoice_id).collect::<Result<Vec<_>>>()?;
            if let Some(id) = invoice_id { invoice_ids.push(id); }
            let category_ids = items.iter().filter_map(|item| item["categoryId"].as_i64()).collect::<Vec<_>>();
            let invoices = indexed_records(tx, "invoices", &invoice_ids, &[("id","id"),("companyScope","companyScope"),("departmentId","departmentId"),("projectId","projectId"),("ownerUserId","ownerUserId")])?;
            let categories = indexed_records(tx, "attachment-categories", &category_ids, &[("id","id"),("name","name")])?;
            for item in &mut items {
                crate::operation::check()?;
                let id = parent_invoice_id(item)?;
                let invoice = invoices.get(&id).ok_or_else(|| error(404,"资料所属发票不存在。"))?;
                set_permissions(item,actor,invoice);
                if let Some(id) = item["categoryId"].as_i64() {
                    if let Some(category) = categories.get(&id) { item["categoryName"] = category["name"].clone(); }
                }
            }
            let (used, can_upload) = if let Some(id) = invoice_id {
                let invoice = invoices.get(&id).ok_or_else(|| error(404, "发票不存在。"))?;
                if !auth::visible(actor, "document.invoices", "view", invoice) { return Err(error(403, "没有访问此发票资料的权限。")); }
                (Some(tx.attachment_bytes(id)?), auth::visible(actor, "document.invoices", "operate", invoice))
            } else { (None, auth::authorize(actor,"document.invoices","operate").is_ok()) };
            Ok(json!({"page":contracts::page(items,total as usize,page,size),"canUpload":can_upload,"usedBytes":used,"fileBytesLimit":store.files.configuration()?.policy.file_bytes,"invoiceBytesLimit":Value::Null}))
        })?
    } else if operation == GET_BUSINESS_ATTACHMENT {
        let id = super::records::id(parameters)?;
        store.read_for(actor, operation, query, |tx, actor| {
            let mut record = store::get(tx, "attachments", id)?;
            let parent = invoice_in(tx, actor, parent_invoice_id(&record)?, "view")?;
            set_permissions(&mut record, actor, &parent);
            let category_id = record["categoryId"].as_i64().ok_or_else(|| unavailable("资料分类编号无效。"))?;
            record["categoryName"] = store::get(tx, "attachment-categories", category_id)?["name"].clone();
            validate_metadata(&record)?;
            let cursor = |name| -> Result<Option<i64>> {
                crate::engine::request_values::find(query,name).map(|value|value.parse::<i64>().ok().filter(|value|*value>0).ok_or_else(||invalid("资料历史游标无效。"))).transpose()
            };
            let revisions = tx.attachment_history(id,"revision",cursor("revisionBefore")?,30)?;
            let events = tx.attachment_history(id,"event",cursor("eventBefore")?,30)?;
            Ok(json!({"attachment":record,"revisions":revisions["items"],"revisionCount":revisions["totalCount"],"revisionNextCursor":revisions["nextCursor"],"eventCount":events["totalCount"],"events":events["items"],"eventNextCursor":events["nextCursor"]}))
        })?
    } else if operation == DOWNLOAD_BUSINESS_ATTACHMENT
        || operation == SAVE_BUSINESS_ATTACHMENT_TO_PATH
    {
        let id = super::records::id(parameters)?;
        let revision = parameters
            .iter()
            .find(|(key, _)| *key == "revision")
            .and_then(|(_, value)| value.parse::<i64>().ok())
            .ok_or_else(|| invalid("缺少有效资料版本号。"))?;
        let (metadata, bytes) = read(store, actor, operation, id, revision)?;
        if operation == DOWNLOAD_BUSINESS_ATTACHMENT {
            return Ok(bytes);
        }
        let path = std::path::PathBuf::from(text(&body, "destinationPath"));
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        let original = std::path::Path::new(metadata["fileName"].as_str().unwrap_or(""));
        let same_extension = match (
            path.extension().and_then(|value| value.to_str()),
            original.extension().and_then(|value| value.to_str()),
        ) {
            (Some(left), Some(right)) => left.eq_ignore_ascii_case(right),
            (None, None) => true,
            _ => false,
        };
        if !same_extension {
            return Err(invalid("请保留原文件扩展名。"));
        }
        paths::atomic_write(&path, &bytes).map_err(unavailable)?;
        json!({"success":true,"message":"文件已保存"})
    } else {
        let id = super::records::id(parameters)?;
        let action = match operation {
            DELETE_BUSINESS_ATTACHMENT => "manage",
            UPDATE_BUSINESS_ATTACHMENT | EDIT_BUSINESS_ATTACHMENT_METADATA => "operate",
            _ => "view",
        };
        match operation {
            UPDATE_BUSINESS_ATTACHMENT
            | EDIT_BUSINESS_ATTACHMENT_METADATA
            | DELETE_BUSINESS_ATTACHMENT => {
                store.transaction_for(actor, operation, query, |transaction, actor| {
                    let mut record = store::get(transaction, "attachments", id)?;
                    let parent =
                        invoice_in(transaction, actor, parent_invoice_id(&record)?, action)?;
                    set_permissions(&mut record, actor, &parent);
                    store::check_version(&record, store::expected(&body))?;
                    required(&body, "note", "变更说明", 500)?;
                    if operation == DELETE_BUSINESS_ATTACHMENT {
                        transaction.delete_blobs(id)?;
                        if !transaction.delete("attachments", id, store::expected(&record))? {
                            return Err(conflict("资料版本已变化，删除已取消。"));
                        }
                        transaction.append_audit(&AuditWrite {
                            kind: "attachments",
                            record_id: id,
                            version: record["versionNumber"].as_i64().unwrap_or(0),
                            action: "delete",
                            actor_id: actor.id,
                            occurred_at: &store::timestamp(),
                            note: &text(&body, "note"),
                        })?;
                        return Ok(json!({"success":true,"message":"资料及全部版本已删除"}));
                    }
                    if operation == UPDATE_BUSINESS_ATTACHMENT {
                        if let Some(revision) = body["currentRevision"].as_i64() {
                            if revision < 1
                                || revision > record["latestRevision"].as_i64().unwrap_or(0)
                            {
                                return Err(invalid("有效版本号不存在。"));
                            }
                        }
                        let event_action = if record["isArchived"] != body["isArchived"] {
                            if body["isArchived"] == true {
                                "Archive"
                            } else {
                                "Restore"
                            }
                        } else {
                            "Confirm"
                        };
                        append_event(transaction, &record, actor, event_action, &body)?;
                        record["currentRevision"] = body["currentRevision"].clone();
                        record["isArchived"] = body["isArchived"].clone();
                    } else {
                        required(&body, "title", "资料名称", 200)?;
                        let category = store::get(
                            transaction,
                            "attachment-categories",
                            body["categoryId"].as_i64().unwrap_or(0),
                        )?;
                        if category["companyScope"] != parent["companyScope"] {
                            return Err(error(403, "不能使用其他公司的资料分类。"));
                        }
                        for key in ["title", "categoryId", "poNumber", "styleNo"] {
                            record[key] = body[key].clone();
                        }
                        record["categoryName"] = category["name"].clone();
                        append_event(transaction, &record, actor, "Edit", &body)?;
                    }
                    store::save(
                        transaction,
                        "attachments",
                        id,
                        record,
                        None,
                        actor,
                        if operation == UPDATE_BUSINESS_ATTACHMENT {
                            "confirm"
                        } else {
                            "edit-metadata"
                        },
                    )
                })?
            }
            _ => return Err(invalid("上传资料需要文件内容。")),
        }
    };
    serde_json::to_vec(&contracts::project(
        contracts::response(operation.id),
        value,
    ))
    .map_err(Into::into)
}

fn set_permissions(record: &mut Value, actor: &Actor, invoice: &Value) {
    record["canEdit"] = json!(auth::visible(
        actor,
        "document.invoices",
        "operate",
        invoice
    ));
    record["canDelete"] = json!(auth::visible(actor, "document.invoices", "manage", invoice));
}
fn parent_invoice_id(record: &Value) -> Result<i64> {
    record["invoiceId"]
        .as_i64()
        .filter(|id| *id > 0)
        .ok_or_else(|| unavailable("资料所属发票编号损坏。"))
}
fn indexed_records(
    tx: &store::Connection<'_>,
    kind: &str,
    ids: &[i64],
    fields: &[(&str, &str)],
) -> Result<BTreeMap<i64, Value>> {
    tx.records_by_ids(kind, ids, fields)?
        .into_iter()
        .map(|row| {
            let id = row["id"]
                .as_i64()
                .ok_or_else(|| unavailable("关联资料编号损坏。"))?;
            Ok((id, row))
        })
        .collect()
}
fn validate_metadata(record: &Value) -> Result<()> {
    if record.get("events").is_some() || record.get("revisions").is_some() {
        return Err(unavailable("资料记录含有无效的内嵌历史。"));
    }
    Ok(())
}
fn append_event(
    tx: &store::Connection<'_>,
    record: &Value,
    actor: &Actor,
    action: &str,
    body: &Value,
) -> Result<()> {
    validate_metadata(record)?;
    tx.append_attachment_history(record["id"].as_i64().ok_or_else(||unavailable("资料编号无效。"))?,"event",&json!({"action":action,"revision":body["currentRevision"],"actorName":actor.name,"note":text(body,"note"),"createdAt":store::timestamp()}))?;
    Ok(())
}
#[cfg(test)]
mod tests;
