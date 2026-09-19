use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    records::{required, text},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::*, paths};
use export_doc_storage::{AuditWrite, BlobWrite};
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use unicode_normalization::UnicodeNormalization;

pub const FILE_LIMIT: usize = 16 * 1024 * 1024;
pub const INVOICE_LIMIT: i64 = 256 * 1024 * 1024;
fn digest(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}
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

fn invoice(store: &Store, actor: &Actor, id: i64, action: &str) -> Result<Value> {
    auth::authorize(actor, "document.invoices", action)?;
    let invoice = store.get("invoices", id)?;
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
    bytes: &[u8],
) -> Result<Value> {
    let invoice = invoice(store, actor, invoice_id, "operate")?;
    let name = file_name.nfc().collect::<String>();
    if !paths::valid_file_name(&name) {
        return Err(invalid("资料文件名无效。"));
    }
    if bytes.is_empty() || bytes.len() > FILE_LIMIT {
        return Err(invalid("每个归档文件须为 1 字节至 16 MiB。"));
    }
    required(&metadata, "title", "资料名称", 200)?;
    required(&metadata, "uploadKey", "上传编号", 100)?;
    let hash = digest(bytes);
    let attachment_id = metadata["attachmentId"].as_i64().unwrap_or(0);
    let category_id = metadata["categoryId"].as_i64().unwrap_or(0);
    let category = store.get("attachment-categories", category_id)?;
    if category["companyScope"] != invoice["companyScope"] {
        return Err(error(403, "资料分类不属于发票所在公司。"));
    }
    store.transaction(|transaction|{
        let attachments=store::all(transaction,"attachments")?;
        if let Some(existing)=attachments.iter().find(|item|item["lastUploadKey"]==metadata["uploadKey"]){
            if existing["invoiceId"]==invoice_id&&existing["lastDigest"]==hash{return Ok(existing.clone());}
            return Err(conflict("同一上传编号已用于不同内容。"));
        }
        if attachment_id==0&&attachments.iter().filter(|item|item["invoiceId"]==invoice_id).count()>=100{return Err(conflict("每张发票最多归档 100 份资料。"));}
        let used=transaction.attachment_bytes(invoice_id)?;
        if used+bytes.len() as i64>INVOICE_LIMIT{return Err(conflict("这张发票的归档资料超过 256 MiB 容量。"));}
        let mut record=if attachment_id>0{
            let previous=store::get(transaction,"attachments",attachment_id)?;
            if previous["invoiceId"]!=invoice_id{return Err(invalid("不能改变资料所属发票。"));}
            if previous["isArchived"]==true{return Err(conflict("请先恢复资料，再上传新版本。"));}
            if previous["categoryId"]!=category_id{return Err(conflict("上传新版本不能修改资料分类，请先修正资料信息。"));}
            for key in ["title","poNumber","styleNo"] { if text(&previous,key)!=text(&metadata,key) {return Err(conflict("上传新版本不能修改资料信息，请先使用修正资料信息。"));} }
            store::check_version(&previous,store::expected(&metadata))?;previous
        }else{contracts::initial(contracts::schema("BusinessAttachmentRecord"))};
        let revision=record["latestRevision"].as_i64().unwrap_or(0)+1;
        if revision>50{return Err(conflict("每份资料最多保留 50 个版本。"));}
        record["invoiceId"]=json!(invoice_id);record["invoiceNo"]=invoice["invoiceNo"].clone();record["invoiceType"]=invoice["type"].clone();record["customerName"]=invoice["customerNameEN"].clone();
        for key in ["title","poNumber","styleNo"]{record[key]=json!(text(&metadata,key));}record["categoryId"]=json!(category_id);
        record["categoryName"]=category["name"].clone();record["latestRevision"]=json!(revision);record["lastUploadKey"]=metadata["uploadKey"].clone();record["lastDigest"]=json!(hash);record["canEdit"]=json!(true);record["canDelete"]=json!(actor.admin);
        let content_type=match std::path::Path::new(&name).extension().and_then(|value|value.to_str()).unwrap_or("").to_ascii_lowercase().as_str(){"pdf"=>"application/pdf","png"=>"image/png","jpg"|"jpeg"=>"image/jpeg","txt"=>"text/plain",_=>"application/octet-stream"};
        let revision_record=json!({"revision":revision,"fileName":name,"contentType":content_type,"length":bytes.len(),"sha256":hash,"uploadedBy":actor.name,"note":text(&metadata,"note"),"createdAt":store::timestamp()});
        if !record["revisions"].is_array(){record["revisions"]=json!([]);}record["revisions"].as_array_mut().unwrap().push(revision_record);
        let record=store::save(transaction,"attachments",attachment_id,record,None,actor,"upload")?;
        transaction.insert_blob(&BlobWrite{kind:&format!("attachment:{revision}"),record_id:record["id"].as_i64().unwrap_or(0),file_name:&name,media_type:content_type,digest:&hash,content:bytes,created_at:&store::timestamp()})?;
        Ok(record)
    })
}
pub fn read(store: &Store, actor: &Actor, id: i64, revision: i64) -> Result<(Value, Vec<u8>)> {
    let record = store.get("attachments", id)?;
    invoice(
        store,
        actor,
        record["invoiceId"].as_i64().unwrap_or(0),
        "view",
    )?;
    let file = store
        .connection()?
        .blob(id, &format!("attachment:{revision}"))?
        .ok_or_else(|| error(404, "资料版本不存在。"))?;
    let (name, content_type, expected_digest, bytes) =
        (file.file_name, file.media_type, file.digest, file.content);
    if digest(&bytes) != expected_digest {
        return Err(unavailable("资料内容校验失败，已停止下载。"));
    }
    Ok((json!({"fileName":name,"contentType":content_type}), bytes))
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
        auth::authorize(actor, "document.invoices", "view")?;
        let invoice_id = query
            .iter()
            .find(|(key, _)| *key == "invoiceId")
            .and_then(|(_, value)| value.parse::<i64>().ok());
        let archived = query
            .iter()
            .any(|(key, value)| *key == "includeArchived" && value == "true");
        let invoices = store.all("invoices")?;
        let categories = store.all("attachment-categories")?;
        let category_id = query
            .iter()
            .find(|(key, _)| *key == "categoryId")
            .and_then(|(_, value)| value.parse::<i64>().ok());
        let items: Vec<_> = store
            .all("attachments")?
            .into_iter()
            .filter(|item| {
                (invoice_id.is_none_or(|id| item["invoiceId"] == id))
                    && category_id.is_none_or(|id| item["categoryId"] == id)
                    && (archived || item["isArchived"] != true)
                    && invoices.iter().any(|invoice| {
                        invoice["id"] == item["invoiceId"]
                            && auth::visible(actor, "document.invoices", "view", invoice)
                    })
            })
            .map(|mut item| {
                if let Some(invoice) = invoices
                    .iter()
                    .find(|invoice| invoice["id"] == item["invoiceId"])
                {
                    set_permissions(&mut item, actor, invoice);
                }
                if let Some(category) = categories
                    .iter()
                    .find(|category| category["id"] == item["categoryId"])
                {
                    item["categoryName"] = category["name"].clone();
                }
                item
            })
            .collect();
        let used = if let Some(id) = invoice_id {
            invoice(store, actor, id, "view")?;
            Some(store.connection()?.attachment_bytes(id)?)
        } else {
            None
        };
        json!({"page":store::paged(items,query),"canUpload":auth::authorize(actor,"document.invoices","operate").is_ok(),"usedBytes":used,"fileBytesLimit":FILE_LIMIT,"invoiceBytesLimit":INVOICE_LIMIT})
    } else {
        let id = super::records::id(parameters)?;
        let mut record = store.get("attachments", id)?;
        let action = match operation {
            DELETE_BUSINESS_ATTACHMENT => "manage",
            UPDATE_BUSINESS_ATTACHMENT | EDIT_BUSINESS_ATTACHMENT_METADATA => "operate",
            _ => "view",
        };
        let parent = invoice(
            store,
            actor,
            record["invoiceId"].as_i64().unwrap_or(0),
            action,
        )?;
        set_permissions(&mut record, actor, &parent);
        match operation {
            GET_BUSINESS_ATTACHMENT => {
                let category = store.get(
                    "attachment-categories",
                    record["categoryId"]
                        .as_i64()
                        .ok_or_else(|| unavailable("资料分类编号无效。"))?,
                )?;
                record["categoryName"] = category["name"].clone();
                let events = record["events"].as_array().cloned().unwrap_or_default();
                json!({"attachment":record,"revisions":record["revisions"],"eventCount":events.len(),"events":events})
            }
            DOWNLOAD_BUSINESS_ATTACHMENT | SAVE_BUSINESS_ATTACHMENT_TO_PATH => {
                let revision = parameters
                    .iter()
                    .find(|(key, _)| *key == "revision")
                    .and_then(|(_, value)| value.parse::<i64>().ok())
                    .ok_or_else(|| invalid("缺少有效资料版本号。"))?;
                let (metadata, bytes) = read(store, actor, id, revision)?;
                if operation == DOWNLOAD_BUSINESS_ATTACHMENT {
                    return Ok(bytes);
                }
                let path = std::path::PathBuf::from(text(&body, "destinationPath"));
                paths::ensure_safe_absolute(&path).map_err(invalid)?;
                if path.extension()
                    != std::path::Path::new(metadata["fileName"].as_str().unwrap_or("")).extension()
                {
                    return Err(invalid("请保留原文件扩展名。"));
                }
                paths::atomic_write(&path, &bytes).map_err(unavailable)?;
                json!({"success":true,"message":"文件已保存"})
            }
            UPDATE_BUSINESS_ATTACHMENT
            | EDIT_BUSINESS_ATTACHMENT_METADATA
            | DELETE_BUSINESS_ATTACHMENT => store.transaction(|transaction| {
                let mut record = store::get(transaction, "attachments", id)?;
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
                        if revision < 1 || revision > record["latestRevision"].as_i64().unwrap_or(0)
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
                    append_event(&mut record, actor, event_action, &body);
                    record["currentRevision"] = body["currentRevision"].clone();
                    record["isArchived"] = body["isArchived"].clone();
                } else {
                    required(&body, "title", "资料名称", 200)?;
                    let category = store::get(
                        transaction,
                        "attachment-categories",
                        body["categoryId"].as_i64().unwrap_or(0),
                    )?;
                    if category["companyScope"] != record["companyScope"] {
                        return Err(error(403, "不能使用其他公司的资料分类。"));
                    }
                    for key in ["title", "categoryId", "poNumber", "styleNo"] {
                        record[key] = body[key].clone();
                    }
                    record["categoryName"] = category["name"].clone();
                    append_event(&mut record, actor, "Edit", &body);
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
            })?,
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
fn append_event(record: &mut Value, actor: &Actor, action: &str, body: &Value) {
    if !record["events"].is_array() {
        record["events"] = json!([]);
    }
    record["events"].as_array_mut().unwrap().push(json!({"action":action,"revision":body["currentRevision"],"actorName":actor.name,"note":text(body,"note"),"createdAt":store::timestamp()}));
}
