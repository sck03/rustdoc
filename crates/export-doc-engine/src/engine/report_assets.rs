//! Owned report resources, shipping marks and seals. Metadata and bytes share
//! the business transaction, so backups and restores retain the same images.
mod references;
use super::{
    auth,
    error::{Result, conflict, error, invalid, unavailable},
    media,
    records::text,
    report_template_files,
    store::{self, Actor, Connection, Store},
    tasks::FileOutput,
};
use crate::generated_api::*;
use base64::{Engine, engine::general_purpose::STANDARD};
use export_doc_report::{RasterImage, ReportData};
use export_doc_storage::BlobWrite;
use references::References;
use serde_json::{Value, json};

const KIND: &str = "report-images";
const MAX_RESOURCE: usize = 32 * 1024 * 1024;
pub const OPERATIONS: &[Operation] = &[
    SAVE_SHIPPING_MARK_IMAGE,
    PREVIEW_SHIPPING_MARK_IMAGE,
    UPLOAD_EXPORTER_SEAL,
    UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
    DOWNLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
    QUERY_REPORT_TEMPLATE_V3_IMAGE_RESOURCES,
    RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
];
pub const UPLOADS: &[Operation] = &[
    UPLOAD_EXPORTER_SEAL,
    UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE,
];

fn resource_id(value: &str) -> Result<&str> {
    let body = value
        .strip_prefix("img-")
        .ok_or_else(|| invalid("图片资源编号无效。"))?;
    let (hash, extension) = body
        .split_once('.')
        .ok_or_else(|| invalid("图片资源编号无效。"))?;
    if hash.len() != 64
        || !hash
            .bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
        || !["png", "jpg"].contains(&extension)
    {
        return Err(invalid("图片资源编号无效。"));
    }
    Ok(value)
}
fn find(tx: &Connection, id: &str) -> Result<Value> {
    resource_id(id)?;
    tx.find_identity(KIND, id)?
        .ok_or_else(|| error(404, "图片资源不存在。"))
}
fn owns(actor: &Actor, resource: &Value) -> bool {
    resource["uploaders"]
        .as_array()
        .is_some_and(|v| v.iter().any(|id| id.as_i64() == Some(actor.id)))
}
pub(super) fn template_visible(actor: &Actor, template: &Value) -> bool {
    super::report_templates::demand_type(actor, &text(template, "reportType")).is_ok()
        && auth::template_visible(actor, "document.report-templates", template)
}
fn schema(content: &str) -> Result<Value> {
    schema_bytes(content.as_bytes())
}
fn schema_bytes(content: &[u8]) -> Result<Value> {
    if let Ok(design) = export_doc_domain::report_template_format::decode(content) {
        return serde_json::to_value(design).map_err(|_| invalid("报表模板结构无效。"));
    }
    let content = std::str::from_utf8(content).map_err(|_| invalid("报表模板文本编码无效。"))?;
    if let Ok(design) = export_doc_domain::designer::Design::from_source(content) {
        return serde_json::to_value(design).map_err(|_| invalid("报表模板结构无效。"));
    }
    Err(invalid("模板必须是统一 .dtpl V3 结构。"))
}
pub(super) fn read(
    tx: &Connection,
    actor: &Actor,
    id: &str,
    paths: Option<&crate::paths::RuntimePaths>,
) -> Result<RasterImage> {
    let entry = find(tx, id)?;
    if !actor.admin
        && !owns(actor, &entry)
        && !References::load(tx, actor, paths)?.readable.contains(id)
    {
        return Err(error(403, "没有读取此图片的权限。"));
    }
    read_entry(tx, &entry)
}
fn read_entry(tx: &Connection, entry: &Value) -> Result<RasterImage> {
    let id = entry["id"]
        .as_i64()
        .ok_or_else(|| unavailable("图片资源记录损坏。"))?;
    let blob = tx
        .blob(id, KIND)?
        .ok_or_else(|| unavailable("图片资源内容缺失。"))?;
    if media::digest(&blob.content) != text(entry, "sha256")
        || blob.digest != text(entry, "sha256")
        || blob.media_type != text(entry, "mediaType")
        || blob.content.len() as u64 != entry["byteLength"].as_u64().unwrap_or(0)
    {
        return Err(unavailable("图片资源内容校验失败。"));
    }
    let media_type = media::image_type(&blob.content, MAX_RESOURCE)
        .map_err(|_| unavailable("已保存的图片损坏。"))?;
    if media_type != blob.media_type {
        return Err(unavailable("图片类型与记录不一致。"));
    }
    Ok(RasterImage {
        media_type: blob.media_type,
        bytes: blob.content,
    })
}
pub(super) fn register(
    tx: &Connection,
    actor: &Actor,
    bytes: &[u8],
    file_name: &str,
    limit: usize,
) -> Result<Value> {
    let media_type = media::image_type(bytes, limit)?;
    let hash = media::digest(bytes);
    let extension = if media_type == "image/png" {
        "png"
    } else {
        "jpg"
    };
    let id = format!("img-{hash}.{extension}");
    let existing = tx.all(KIND)?;
    let previous = existing.iter().find(|v| v["resourceId"] == id).cloned();
    if let Some(mut previous) = previous {
        read_entry(tx, &previous)?;
        if !owns(actor, &previous) {
            previous["uploaders"]
                .as_array_mut()
                .ok_or_else(|| unavailable("图片归属记录损坏。"))?
                .push(json!(actor.id));
            let record_id = previous["id"].as_i64().unwrap();
            return store::save(tx, KIND, record_id, previous, Some(id), actor, "image-own");
        }
        return Ok(previous);
    }
    if existing.len() >= 1000 {
        return Err(conflict("图片资源已达到 1000 个，请回收未使用图片后重试。"));
    }
    let record = store::save(
        tx,
        KIND,
        0,
        json!({"resourceId":id,"mediaType":media_type,"sha256":hash,"byteLength":bytes.len(),"altText":file_name,"uploaders":[actor.id]}),
        Some(id.clone()),
        actor,
        "image-upload",
    )?;
    tx.insert_blob(&BlobWrite {
        kind: KIND,
        record_id: record["id"].as_i64().unwrap(),
        file_name: &id,
        media_type,
        digest: &hash,
        content: bytes,
        created_at: &store::timestamp(),
    })?;
    Ok(record)
}
fn response(record: &Value) -> Value {
    json!({"id":record["resourceId"],"mediaType":record["mediaType"],"byteLength":record["byteLength"],"sha256":record["sha256"],"altText":record["altText"],"storagePolicy":"受控图片和归属保存在业务数据库，随备份恢复。"})
}

pub fn upload(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !crate::paths::valid_file_name(name) {
        return Err(invalid("图片文件名无效。"));
    }
    if operation == UPLOAD_REPORT_TEMPLATE_V3_IMAGE_RESOURCE {
        return store.transaction(|tx| {
            register(tx, actor, bytes, name, MAX_RESOURCE).map(|v| response(&v))
        });
    }
    let id = super::records::id(parameters)?;
    let kind = parameters
        .iter()
        .find(|(key, _)| *key == "sealType")
        .map(|(_, v)| v.as_str())
        .unwrap_or("");
    let field = match kind {
        "document" | "Document" => "docSealPath",
        "customs" | "Customs" => "customsSealPath",
        _ => return Err(invalid("印章类别只能是 document 或 customs。")),
    };
    store.transaction(|tx| {
        let mut exporter = store::get(tx, "exporters", id)?;
        if !auth::visible(actor, "document.master-data", "edit", &exporter) {
            return Err(error(403, "没有修改此出口商印章的权限。"));
        }
        let image = register(tx, actor, bytes, name, 5 * 1024 * 1024)?;
        exporter[field] = json!(format!("Files/Seals/{}", text(&image, "resourceId")));
        let identity = text(&exporter, "exporterNameEN");
        store::save(
            tx,
            "exporters",
            id,
            exporter,
            Some(identity),
            actor,
            "seal-upload",
        )
    })
}

pub fn handle(
    service: &super::NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let store = &service.store;
    match operation {
        SAVE_SHIPPING_MARK_IMAGE => {
            if auth::authorize(actor, "document.invoices", "edit").is_err() {
                auth::authorize(actor, "document.invoices", "create")?;
            }
            let url = text(body, "imageDataUrl");
            let (kind, encoded) = url
                .split_once(";base64,")
                .ok_or_else(|| invalid("唛头必须是 PNG/JPEG 图片数据。"))?;
            if !["data:image/png", "data:image/jpeg"].contains(&kind)
                || encoded.len() > 7 * 1024 * 1024
            {
                return Err(invalid("唛头图片类型无效或超过 5 MiB。"));
            }
            let bytes = STANDARD
                .decode(encoded)
                .map_err(|_| invalid("唛头图片编码无效。"))?;
            let decoded = media::image_type(&bytes, 5 * 1024 * 1024)?;
            if kind != format!("data:{decoded}") {
                return Err(invalid("图片内容与声明类型不一致。"));
            }
            let saved =
                store.transaction(|tx| register(tx, actor, &bytes, "唛头", 5 * 1024 * 1024))?;
            Ok(
                json!({"imagePath":format!("Files/ShippingMarks/{}",text(&saved,"resourceId")),"fileName":saved["resourceId"],"contentType":saved["mediaType"],"sizeBytes":saved["byteLength"],"storagePolicy":"唛头保存在业务数据库，随备份恢复。"}),
            )
        }
        PREVIEW_SHIPPING_MARK_IMAGE => {
            auth::authorize(actor, "document.invoices", "view")?;
            let path = text(body, "imagePath");
            let id = stored_id(&path, "Files/ShippingMarks/")?;
            let image = read(&*store.connection()?, actor, id, None)?;
            Ok(
                json!({"imagePath":path,"fileName":id,"contentType":image.media_type,"sizeBytes":image.bytes.len(),"dataUrl":image.data_url()?,"storagePolicy":"受控唛头预览"}),
            )
        }
        QUERY_REPORT_TEMPLATE_V3_IMAGE_RESOURCES => {
            let _access = report_template_files::storage_lock(&service.paths)?;
            let tx = store.connection()?;
            let references = References::load(&tx, actor, Some(&service.paths))?;
            let mut items = vec![];
            for entry in tx.all(KIND)? {
                let id = text(&entry, "resourceId");
                let own = owns(actor, &entry);
                if !actor.admin && !own && !references.readable.contains(&id) {
                    continue;
                }
                let is_referenced = references.all.contains(&id);
                items.push(json!({"id":entry["resourceId"],"mediaType":entry["mediaType"],"byteLength":entry["byteLength"],"sha256":entry["sha256"],"ownsUpload":own,"isReferenced":is_referenced,"canRecycle":(actor.admin||own)&&!is_referenced}));
            }
            Ok(store::paged(items, query))
        }
        RECYCLE_REPORT_TEMPLATE_V3_IMAGE_RESOURCE => {
            let id = parameter_id(parameters)?;
            let _access = report_template_files::storage_lock(&service.paths)?;
            store.transaction(|tx| {
                let mut entry = find(tx, id)?;
                if !actor.admin && !owns(actor, &entry) {
                    return Err(error(403, "只能回收自己上传的图片。"));
                }
                if References::load(tx, actor, Some(&service.paths))?
                    .all
                    .contains(id)
                {
                    return Err(conflict(
                        "图片仍被单据、印章、模板或模板历史引用，不能回收。",
                    ));
                }
                let record_id = entry["id"].as_i64().unwrap();
                entry["uploaders"]
                    .as_array_mut()
                    .ok_or_else(|| unavailable("图片归属记录损坏。"))?
                    .retain(|v| v.as_i64() != Some(actor.id));
                if actor.admin || entry["uploaders"].as_array().is_some_and(Vec::is_empty) {
                    tx.delete_blob(record_id, KIND)?;
                    if !tx.delete(KIND, record_id, entry["versionNumber"].as_i64().unwrap())? {
                        return Err(conflict("图片已变化，请刷新。"));
                    }
                } else {
                    store::save(
                        tx,
                        KIND,
                        record_id,
                        entry,
                        Some(id.into()),
                        actor,
                        "image-recycle",
                    )?;
                }
                Ok(json!({"success":true,"message":"图片已回收。"}))
            })
        }
        _ => Err(invalid("不是图片资源操作。")),
    }
}
fn parameter_id<'a>(parameters: &'a [(&str, String)]) -> Result<&'a str> {
    parameters
        .iter()
        .find(|(k, _)| *k == "resourceId")
        .map(|(_, v)| v.as_str())
        .ok_or_else(|| invalid("缺少图片编号。"))
}
fn stored_id<'a>(path: &'a str, prefix: &str) -> Result<&'a str> {
    resource_id(
        path.strip_prefix(prefix)
            .ok_or_else(|| invalid("只允许使用受控图片引用。"))?,
    )
}
pub fn download(
    service: &super::NativeService,
    actor: &Actor,
    parameters: &[(&str, String)],
) -> Result<FileOutput> {
    let id = parameter_id(parameters)?;
    let _access = report_template_files::storage_lock(&service.paths)?;
    let image = read(
        &*service.store.connection()?,
        actor,
        id,
        Some(&service.paths),
    )?;
    Ok(FileOutput {
        file_name: id.into(),
        media_type: image.media_type,
        content: image.bytes,
    })
}

pub fn validate_invoice(tx: &Connection, actor: &Actor, value: &mut Value) -> Result<()> {
    match text(value, "shippingMarksType").as_str() {
        "" | "Text" => {
            value["shippingMarksType"] = json!("Text");
            value["shippingMarksImage"] = json!("");
        }
        "Image" => {
            let path = text(value, "shippingMarksImage");
            let id = stored_id(&path, "Files/ShippingMarks/")?;
            read(tx, actor, id, None)?;
            value["shippingMarks"] = json!("");
        }
        _ => return Err(invalid("唛头类型只能是文字或图片。")),
    }
    Ok(())
}
pub fn validate_exporter(tx: &Connection, actor: &Actor, value: &Value) -> Result<()> {
    for field in ["docSealPath", "customsSealPath"] {
        let path = text(value, field);
        if !path.is_empty() {
            read(tx, actor, stored_id(&path, "Files/Seals/")?, None)?;
        }
    }
    Ok(())
}
pub fn hydrate(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    data: &mut ReportData,
    content: Option<&[u8]>,
) -> Result<()> {
    let _access = report_template_files::storage_lock(paths)?;
    let tx = store.connection()?;
    if data.text("Invoice.ShippingMarksType") == "Image" {
        let reference = data.text("Invoice.ShippingMarksImage");
        let id = stored_id(&reference, "Files/ShippingMarks/")?;
        data.images
            .insert("Invoice.ShippingMarks".into(), read(&tx, actor, id, None)?);
    }
    if data.root["ShowSeal"] == true {
        for (source, field) in [
            ("Exporter.DocSealPath", "doc_seal_path"),
            ("Exporter.CustomsSealPath", "customs_seal_path"),
        ] {
            let path = data.text(source);
            if !path.is_empty() {
                let id = stored_id(&path, "Files/Seals/")?;
                data.images
                    .insert(field.into(), read(&tx, actor, id, None)?);
            }
        }
    }
    if let Some(content) = content {
        template_images(&tx, actor, content, Some(paths), |id, image| {
            data.images.insert(id, image);
        })?;
    }
    Ok(())
}
pub fn validate_template(
    tx: &Connection,
    actor: &Actor,
    content: &str,
    paths: Option<&crate::paths::RuntimePaths>,
) -> Result<()> {
    template_images(tx, actor, content.as_bytes(), paths, |_, _| {})
}

fn template_images(
    tx: &Connection,
    actor: &Actor,
    content: &[u8],
    paths: Option<&crate::paths::RuntimePaths>,
    mut consume: impl FnMut(String, RasterImage),
) -> Result<()> {
    let schema = schema_bytes(content)?;
    let mut references = None;
    for resource in schema["resources"].as_array().into_iter().flatten() {
        crate::operation::check()?;
        let id = text(resource, "id");
        let entry = find(tx, &id)?;
        if !actor.admin && !owns(actor, &entry) {
            if references.is_none() {
                references = Some(References::load(tx, actor, paths)?);
            }
            if !references.as_ref().unwrap().readable.contains(&id) {
                return Err(error(403, "没有读取此图片的权限。"));
            }
        }
        let image = read_entry(tx, &entry)?;
        if resource["sha256"] != media::digest(&image.bytes)
            || resource["byteLength"].as_u64() != Some(image.bytes.len() as u64)
            || resource["mediaType"] != image.media_type
        {
            return Err(invalid("模板图片清单与受控资源不一致。"));
        }
        consume(id, image);
    }
    Ok(())
}
