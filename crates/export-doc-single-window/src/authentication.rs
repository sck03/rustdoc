use crate::{Result, digest, prefixed};
use base64::{
    Engine,
    prelude::{BASE64_STANDARD, BASE64_URL_SAFE_NO_PAD},
};
use export_doc_domain::single_window::text;
use hmac::{Hmac, KeyInit, Mac};
use serde_json::Value;
use sha2::Sha256;
use zeroize::Zeroizing;
fn key(secret: &str) -> Result<Zeroizing<Vec<u8>>> {
    let bytes = Zeroizing::new(
        BASE64_STANDARD
            .decode(secret.trim())
            .map_err(|_| "交接密钥编码无效。")?,
    );
    if bytes.len() != 32 {
        return Err("交接密钥须为 32 字节。".into());
    }
    Ok(bytes)
}
pub fn assignment(code: &str) -> Result<Value> {
    let code = code.trim();
    if code.len() > 4096 {
        return Err("持卡机授权码超过容量。".into());
    }
    let encoded = code
        .strip_prefix("SWAC1.")
        .ok_or("持卡机授权码格式无效。")?;
    let bytes = Zeroizing::new(
        BASE64_URL_SAFE_NO_PAD
            .decode(encoded)
            .map_err(|_| "持卡机授权码无法解析。")?,
    );
    let value: Value = serde_json::from_slice(&bytes).map_err(|_| "持卡机授权码内容无效。")?;
    validate_assignment(&value)?;
    Ok(value)
}
pub fn encode_assignment(value: &Value) -> Result<String> {
    validate_assignment(value)?;
    Ok(format!(
        "SWAC1.{}",
        BASE64_URL_SAFE_NO_PAD.encode(serde_json::to_vec(value).map_err(|e| e.to_string())?)
    ))
}
fn validate_assignment(value: &Value) -> Result<()> {
    if value["version"] != 1
        || !prefixed(text(value, "stationKey"), "SWS-")
        || !prefixed(text(value, "profileKey"), "SWP-")
        || value["canSubmitCustomsCoo"] != true && value["canSubmitAgentConsignment"] != true
    {
        return Err("持卡机授权码的机器、档案或业务权限无效。".into());
    }
    for (field, max) in [
        ("profileName", 80),
        ("companyScope", 120),
        ("cardIdentifier", 120),
    ] {
        let v = text(value, field);
        if v.is_empty() || v.encode_utf16().count() > max || v.chars().any(char::is_control) {
            return Err("持卡机授权码缺少有效的公司或操作卡信息。".into());
        }
    }
    key(text(value, "authenticationSecret"))?;
    Ok(())
}
fn append(out: &mut String, value: &str) {
    out.push_str(&format!("{}:{value}|", value.encode_utf16().count()));
}
fn append_fields(out: &mut String, value: &Value, fields: &[&str]) {
    for field in fields {
        let input = &value[*field];
        if let Some(s) = input.as_str() {
            append(out, s);
        } else if input.is_number() {
            append(out, &input.to_string());
        } else {
            append(out, "");
        }
    }
}
pub fn content_digest(manifest: &Value) -> Result<String> {
    let mut out = String::new();
    append_fields(
        &mut out,
        manifest,
        &[
            "schemaVersion",
            "packageId",
            "packageType",
            "businessType",
            "batchReference",
            "sourceInvoiceId",
            "sourceDocumentId",
            "sourceDocumentType",
            "submissionVersion",
            "draftRevision",
            "sourceBaselineHash",
            "invoiceNo",
            "contractNo",
            "companyScope",
            "snapshotSha256",
            "sourcePackageDigest",
            "receiptReferenceNo",
            "stationKey",
            "cardIdentifier",
            "clientProfileKey",
            "clientProfileName",
            "assignmentNonce",
            "authenticationAlgorithm",
        ],
    );
    let time = chrono::DateTime::parse_from_rfc3339(text(manifest, "createdAt"))
        .map_err(|_| "交接包创建时间缺少时区或格式无效。")?
        .with_timezone(&chrono::Utc);
    let instant = format!(
        "{}.{:07}+00:00",
        time.format("%Y-%m-%dT%H:%M:%S"),
        time.timestamp_subsec_nanos() / 100
    );
    append(&mut out, &instant);
    append_fields(&mut out, manifest, &["createdOnMachine"]);
    for name in ["payloadFiles", "attachmentFiles"] {
        let mut files: Vec<_> = manifest[name].as_array().into_iter().flatten().collect();
        files.sort_by(|a, b| {
            text(a, "relativePath")
                .encode_utf16()
                .cmp(text(b, "relativePath").encode_utf16())
        });
        for file in files {
            append_fields(
                &mut out,
                file,
                &[
                    "relativePath",
                    "mediaType",
                    "description",
                    "sizeBytes",
                    "sha256",
                ],
            );
        }
    }
    let mut warnings: Vec<_> = manifest["warnings"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(Value::as_str)
        .collect();
    warnings.sort_by(|a, b| a.encode_utf16().cmp(b.encode_utf16()));
    for warning in warnings {
        append(&mut out, warning);
    }
    Ok(digest(out.as_bytes()))
}
fn authentication_payload(manifest: &Value) -> String {
    let mut out = String::new();
    append_fields(
        &mut out,
        manifest,
        &[
            "schemaVersion",
            "packageId",
            "packageType",
            "businessType",
            "batchReference",
            "contentDigest",
            "sourcePackageDigest",
            "stationKey",
            "clientProfileKey",
            "cardIdentifier",
            "companyScope",
            "assignmentNonce",
            "authenticationAlgorithm",
        ],
    );
    out
}
pub fn sign(manifest: &Value, secret: &str) -> Result<String> {
    let mut hmac =
        Hmac::<Sha256>::new_from_slice(&key(secret)?).map_err(|_| "交接密钥长度错误。")?;
    hmac.update(authentication_payload(manifest).as_bytes());
    Ok(hmac
        .finalize()
        .into_bytes()
        .iter()
        .map(|b| format!("{b:02X}"))
        .collect())
}
pub fn verify(manifest: &Value, secret: &str) -> Result<()> {
    let tag = text(manifest, "authenticationTag");
    if !crate::hex(tag, 64) {
        return Err("交接包认证码格式错误。".into());
    }
    let tag: Vec<_> = (0..64)
        .step_by(2)
        .map(|i| u8::from_str_radix(&tag[i..i + 2], 16).expect("validated hex"))
        .collect();
    let mut hmac =
        Hmac::<Sha256>::new_from_slice(&key(secret)?).map_err(|_| "交接密钥长度错误。")?;
    hmac.update(authentication_payload(manifest).as_bytes());
    hmac.verify_slice(&tag)
        .map_err(|_| "交接包认证失败，内容、公司或目标持卡机不匹配。".into())
}
