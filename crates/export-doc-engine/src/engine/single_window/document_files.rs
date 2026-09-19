use super::*;
use export_doc_single_window::digest;
use export_doc_storage::BlobWrite;
use std::collections::BTreeMap;

const PREFIX: &str = "sw-attachment:";
pub const SINGLE: usize = 8 * 1024 * 1024;

pub fn prepare(
    tx: &Connection,
    document_id: i64,
    body: &mut Value,
) -> Result<BTreeMap<String, Vec<u8>>> {
    let mut files = BTreeMap::new();
    let mut total = 0;
    let Some(rows) = body["attachments"].as_array_mut() else {
        return Ok(files);
    };
    if rows.len() > 20 {
        return Err(invalid("单一窗口附件最多 20 个。"));
    }
    for (index, row) in rows.iter_mut().enumerate() {
        crate::operation::check()?;
        let path = rules::text(row, "filePath");
        let bytes = if let Some(hash) = path.strip_prefix(PREFIX) {
            if !export_doc_single_window::hex(hash, 64) {
                return Err(invalid("附件资源编号无效。"));
            }
            tx.blob(document_id, path)?
                .ok_or_else(|| error(404, "单证附件不存在。"))?
                .content
        } else {
            if tx.provider() != "SQLite" {
                return Err(invalid("网页申报不能读取服务器本地文件。"));
            }
            super::super::media::read_local(std::path::Path::new(path), SINGLE)?
        };
        if bytes.is_empty() || bytes.len() > SINGLE {
            return Err(invalid("附件不能为空，单个附件最多 8 MiB。"));
        }
        total += bytes.len();
        if total > 20 * 1024 * 1024 {
            return Err(invalid("单证附件合计最多 20 MiB。"));
        }
        let name = rules::text(row, "fileName");
        if !crate::paths::valid_file_name(name) {
            return Err(invalid("附件文件名无效。"));
        }
        let key = format!("{PREFIX}{}", digest(&bytes));
        if path.starts_with(PREFIX) && path != key {
            return Err(unavailable("单证附件内容摘要不匹配。"));
        }
        row["filePath"] = json!(key);
        row["fileExistsAtBuild"] = json!(true);
        row["sortOrder"] = json!(index + 1);
        files.insert(key, bytes);
    }
    Ok(files)
}

pub fn persist(tx: &Connection, document: &Value, files: &BTreeMap<String, Vec<u8>>) -> Result<()> {
    let id = document["id"]
        .as_i64()
        .ok_or_else(|| unavailable("单证编号损坏。"))?;
    for (key, bytes) in files {
        if tx.blob(id, key)?.is_some() {
            continue;
        }
        let row = document["attachments"]
            .as_array()
            .into_iter()
            .flatten()
            .find(|r| r["filePath"] == *key)
            .ok_or_else(|| unavailable("附件元数据缺失。"))?;
        tx.insert_blob(&BlobWrite {
            kind: key,
            record_id: id,
            file_name: rules::text(row, "fileName"),
            media_type: rules::text(row, "mediaType"),
            digest: &digest(bytes),
            content: bytes,
            created_at: &store::timestamp(),
        })?;
    }
    Ok(())
}

pub fn payload(document: &Value, row: &Value, bytes: &[u8]) -> Vec<u8> {
    use base64::{Engine, prelude::BASE64_STANDARD};
    let mut out = String::from("<?xml version=\"1.0\" encoding=\"UTF-8\"?><File>");
    let mut field = |tag: &str, value: &str| {
        out.push_str(&format!(
            "<{tag}>{}</{tag}>",
            value
                .replace('&', "&amp;")
                .replace('<', "&lt;")
                .replace('>', "&gt;")
        ));
    };
    for (tag, key) in [
        ("CertNo", "certNo"),
        ("CertType", "certType"),
        ("AplRegNo", "aplRegNo"),
        ("CiqRegNo", "ciqRegNo"),
    ] {
        field(
            tag,
            if rules::text(row, key).is_empty() {
                rules::text(document, key)
            } else {
                rules::text(row, key)
            },
        );
    }
    let extension = std::path::Path::new(rules::text(row, "fileName"))
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    field(
        "FileType",
        if rules::text(row, "fileType").is_empty() {
            &extension
        } else {
            rules::text(row, "fileType")
        },
    );
    field("FileName", rules::text(row, "fileName"));
    field(
        "DocType",
        if rules::text(row, "docType").is_empty() {
            "1"
        } else {
            rules::text(row, "docType")
        },
    );
    field("FileContent", &BASE64_STANDARD.encode(bytes));
    field("IsDelay", if row["isDelay"] == true { "1" } else { "0" });
    out.push_str("</File>");
    out.into_bytes()
}
