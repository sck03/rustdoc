use crate::{Check, Result, authentication, digest, hex, prefixed, protocol_token};
use export_doc_domain::single_window::{Business, text};
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    io::{Cursor, Read, Write},
};
use unicode_normalization::UnicodeNormalization;
pub const MAX_PACKAGE: usize = 100 * 1024 * 1024;
const MAX_EXPANDED: u64 = 256 * 1024 * 1024;
pub struct Package {
    pub manifest: Value,
    pub files: BTreeMap<String, Vec<u8>>,
}
pub fn wire(value: &Value, upper: bool) -> Value {
    match value {
        Value::Object(values) => Value::Object(
            values
                .iter()
                .map(|(key, value)| {
                    let mut chars = key.chars();
                    let first = chars.next().unwrap_or_default();
                    let key = if upper {
                        first.to_uppercase().collect::<String>()
                    } else {
                        first.to_lowercase().collect::<String>()
                    } + chars.as_str();
                    (key, wire(value, upper))
                })
                .collect(),
        ),
        Value::Array(values) => json!(values.iter().map(|v| wire(v, upper)).collect::<Vec<_>>()),
        _ => value.clone(),
    }
}
fn safe_path(name: &str) -> bool {
    if name.is_empty()
        || name.len() > 500
        || name.nfc().collect::<String>() != name
        || name.contains(['\\', ':', '\0'])
    {
        return false;
    }
    name.split('/').all(|part| {
        !part.is_empty()
            && !matches!(part, "." | "..")
            && !part.ends_with(['.', ' '])
            && !part
                .chars()
                .any(|c| c.is_control() || matches!(c, '<' | '>' | '"' | '|' | '?' | '*'))
            && !matches!(
                part.split('.').next().unwrap_or("").to_uppercase().as_str(),
                "CON"
                    | "PRN"
                    | "AUX"
                    | "NUL"
                    | "COM1"
                    | "COM2"
                    | "COM3"
                    | "COM4"
                    | "COM5"
                    | "COM6"
                    | "COM7"
                    | "COM8"
                    | "COM9"
                    | "LPT1"
                    | "LPT2"
                    | "LPT3"
                    | "LPT4"
                    | "LPT5"
                    | "LPT6"
                    | "LPT7"
                    | "LPT8"
                    | "LPT9"
            )
    })
}
pub fn describe(name: &str, content: &[u8], media: &str, description: &str) -> Result<Value> {
    if !safe_path(name) {
        return Err("交接包文件名不安全。".into());
    }
    Ok(
        json!({"relativePath":name,"mediaType":media,"description":description,"sizeBytes":content.len(),"sha256":digest(content)}),
    )
}
impl Package {
    pub fn seal(
        mut manifest: Value,
        files: BTreeMap<String, Vec<u8>>,
        secret: &str,
        check: Check<'_>,
    ) -> Result<Self> {
        manifest["contentDigest"] = json!(authentication::content_digest(&manifest)?);
        manifest["authenticationTag"] = json!(authentication::sign(&manifest, secret)?);
        let package = Self { manifest, files };
        package.validate(check)?;
        Ok(package)
    }
    pub fn encode(&self, check: Check<'_>) -> Result<Vec<u8>> {
        self.validate(check)?;
        let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default()
            .compression_method(zip::CompressionMethod::Deflated);
        zip.start_file("manifest.json", options)
            .map_err(|e| e.to_string())?;
        zip.write_all(
            &serde_json::to_vec_pretty(&wire(&self.manifest, true)).map_err(|e| e.to_string())?,
        )
        .map_err(|e| e.to_string())?;
        for (name, bytes) in &self.files {
            check()?;
            zip.start_file(name, options).map_err(|e| e.to_string())?;
            zip.write_all(bytes).map_err(|e| e.to_string())?;
        }
        let bytes = zip.finish().map_err(|e| e.to_string())?.into_inner();
        if bytes.len() > MAX_PACKAGE {
            return Err("交接包超过 100 MiB。".into());
        }
        Ok(bytes)
    }
    pub fn decode(bytes: &[u8], expected: &str, check: Check<'_>) -> Result<Self> {
        if bytes.is_empty() || bytes.len() > MAX_PACKAGE {
            return Err("交接包为空或超过 100 MiB。".into());
        }
        let mut zip =
            zip::ZipArchive::new(Cursor::new(bytes)).map_err(|_| "交接包不是有效 ZIP。")?;
        if zip.len() > 512 {
            return Err("交接包文件数量超过 512。".into());
        }
        let mut names = BTreeSet::new();
        let mut files = BTreeMap::new();
        let mut size = 0;
        for index in 0..zip.len() {
            check()?;
            let mut entry = zip.by_index(index).map_err(|e| e.to_string())?;
            let name = entry.name().trim_end_matches('/').to_owned();
            if !safe_path(&name)
                || !names.insert(name.to_uppercase())
                || entry
                    .unix_mode()
                    .is_some_and(|mode| !matches!(mode & 0o170000, 0 | 0o100000 | 0o040000))
            {
                return Err("交接包包含重复、不安全路径或链接。".into());
            }
            if entry.is_dir() {
                continue;
            }
            size += entry.size();
            if size > MAX_EXPANDED || entry.size() > MAX_PACKAGE as u64 {
                return Err("交接包解压容量超限。".into());
            }
            let mut content = Vec::new();
            (&mut entry)
                .take(MAX_PACKAGE as u64 + 1)
                .read_to_end(&mut content)
                .map_err(|e| e.to_string())?;
            if content.len() as u64 != entry.size() {
                return Err("交接包文件长度不一致。".into());
            }
            files.insert(name, content);
        }
        let bytes = files
            .remove("manifest.json")
            .ok_or("交接包缺少 manifest.json。")?;
        if bytes.len() > 4 * 1024 * 1024 {
            return Err("交接清单超过容量。".into());
        }
        let manifest = wire(
            &serde_json::from_slice(&bytes).map_err(|_| "交接清单不是有效 JSON。")?,
            false,
        );
        let package = Self { manifest, files };
        if package.manifest["packageType"] != expected {
            return Err("交接包类型与当前操作不一致。".into());
        }
        package.validate(check)?;
        Ok(package)
    }
    fn validate(&self, check: Check<'_>) -> Result<()> {
        let m = &self.manifest;
        export_doc_contracts::validation::structure(
            export_doc_contracts::contracts::schema("SingleWindowPackageManifest"),
            m,
        )?;
        Business::parse(text(m, "businessType"))?;
        if m["schemaVersion"] != "4.0"
            || !["SubmitPackage", "ReceiptPackage"].contains(&text(m, "packageType"))
            || !hex(text(m, "packageId"), 32)
            || !protocol_token(text(m, "batchReference"), 40)
            || !prefixed(text(m, "stationKey"), "SWS-")
            || !prefixed(text(m, "clientProfileKey"), "SWP-")
            || !hex(text(m, "assignmentNonce"), 32)
            || m["authenticationAlgorithm"] != "HMAC-SHA256"
            || !hex(text(m, "authenticationTag"), 64)
        {
            return Err("交接包版本、批次或认证绑定信息无效。".into());
        }
        for key in [
            "sourceInvoiceId",
            "sourceDocumentId",
            "submissionVersion",
            "draftRevision",
        ] {
            if m[key].as_i64().is_none_or(|n| n <= 0) {
                return Err("交接包缺少有效来源版本。".into());
            }
        }
        for (key, max) in [
            ("sourceDocumentType", 80),
            ("companyScope", 120),
            ("cardIdentifier", 120),
            ("clientProfileName", 80),
        ] {
            if text(m, key).is_empty() || text(m, key).encode_utf16().count() > max {
                return Err("交接包目标公司或持卡机绑定无效。".into());
            }
        }
        let submit = m["packageType"] == "SubmitPackage";
        if submit {
            if !text(m, "receiptReferenceNo").is_empty()
                || !text(m, "sourcePackageDigest").is_empty()
            {
                return Err("提交包不得携带回执状态。".into());
            }
        } else if !hex(text(m, "sourcePackageDigest"), 64)
            || !text(m, "snapshotSha256").is_empty()
            || m["attachmentFiles"]
                .as_array()
                .is_none_or(|a| !a.is_empty())
        {
            return Err("回执包来源摘要或附件清单无效。".into());
        }
        let mut declared = BTreeSet::new();
        let mut size = 0u64;
        if submit {
            let content = self
                .files
                .get("snapshot.json")
                .ok_or("提交包缺少来源快照。")?;
            if digest(content) != text(m, "snapshotSha256").to_uppercase() {
                return Err("来源快照摘要不匹配。".into());
            }
            declared.insert("snapshot.json".to_owned());
        }
        for group in ["payloadFiles", "attachmentFiles"] {
            let rows = m[group].as_array().ok_or("交接文件清单无效。")?;
            if group == "payloadFiles" && rows.is_empty() {
                return Err("交接包缺少业务报文。".into());
            }
            for row in rows {
                check()?;
                let name = text(row, "relativePath");
                let prefix = if group == "attachmentFiles" {
                    "attachments/"
                } else if submit {
                    "payloads/"
                } else {
                    "receipts/"
                };
                if !safe_path(name) || !name.starts_with(prefix) || !declared.insert(name.into()) {
                    return Err("交接报文路径或重复声明无效。".into());
                }
                let content = self.files.get(name).ok_or("交接清单声明的文件不存在。")?;
                size += content.len() as u64;
                if size > MAX_EXPANDED
                    || row["sizeBytes"].as_u64() != Some(content.len() as u64)
                    || digest(content) != text(row, "sha256").to_uppercase()
                {
                    return Err("交接文件容量或摘要不匹配。".into());
                }
                if group == "payloadFiles" {
                    crate::xml::validate(content)?;
                }
            }
        }
        if self.files.keys().any(|name| !declared.contains(name)) {
            return Err("交接包包含未声明文件。".into());
        }
        if authentication::content_digest(m)? != text(m, "contentDigest").to_uppercase() {
            return Err("交接包内容摘要不匹配。".into());
        }
        Ok(())
    }
}
