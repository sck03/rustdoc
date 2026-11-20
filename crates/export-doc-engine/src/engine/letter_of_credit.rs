//! File extraction is a preview. Only the invoice save command persists its text.
use super::{
    NativeService,
    error::{Result, error, invalid},
    media,
    records::text,
};
use crate::{generated_api::*, pdf};
use serde_json::{Value, json};
use std::path::Path;

pub const OPERATIONS: &[Operation] = &[
    IMPORT_LETTER_OF_CREDIT_DOCUMENT,
    UPLOAD_LETTER_OF_CREDIT_DOCUMENT,
];
const MAX_BYTES: usize = 25 * 1024 * 1024;
const MAX_CHARACTERS: usize = 500_000;

pub fn local(service: &NativeService, body: &Value) -> Result<Value> {
    let source = text(body, "filePath");
    let bytes = media::read_local(Path::new(&source), MAX_BYTES)?;
    import(service, &source, &bytes)
}
pub fn import(service: &NativeService, source: &str, bytes: &[u8]) -> Result<Value> {
    if bytes.is_empty() || bytes.len() > MAX_BYTES {
        return Err(error(413, "信用证文件为空或超过 25 MiB。"));
    }
    let extension = Path::new(source)
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let (content, description) = match extension.as_str() {
        "txt" | "md" | "csv" | "json" | "xml" => (decode_text(bytes)?, "文本文件"),
        "png" | "jpg" | "jpeg" | "bmp" | "tif" | "tiff" | "webp" => {
            (ocr(service, source, bytes)?, "图片 OCR")
        }
        "pdf" => {
            #[cfg(feature = "ocr")]
            let _lease = service.ocr_gate.try_lock().map_err(|cause| match cause {
                std::sync::TryLockError::WouldBlock => {
                    error(429, "OCR 正在处理其他文件，请稍后重试。")
                }
                std::sync::TryLockError::Poisoned(_) => {
                    super::error::unavailable("OCR 执行状态异常。")
                }
            })?;
            (pdf::extract_text(bytes, &service.paths)?, "PDF")
        }
        _ => return Err(invalid("请选择文本、PDF 或受支持的图片信用证文件。")),
    };
    let content = content
        .replace("\r\n", "\n")
        .replace('\r', "\n")
        .replace('\0', "");
    let content = content.trim();
    if content.is_empty() {
        return Err(invalid("未能从信用证文件中提取有效文本。"));
    }
    if content.chars().count() > MAX_CHARACTERS {
        return Err(invalid("信用证提取文本超过 50 万字，请拆分文件。"));
    }
    Ok(
        json!({"sourcePath":source,"sourceDescription":description,"extractedText":content,"storagePolicy":"仅返回提取预览；确认保存发票后才持久化信用证文本。"}),
    )
}
fn decode_text(bytes: &[u8]) -> Result<String> {
    if bytes.starts_with(&[0xff, 0xfe]) || bytes.starts_with(&[0xfe, 0xff]) {
        if bytes.len() % 2 != 0 {
            return Err(invalid("UTF-16 文本长度无效。"));
        }
        let little = bytes[0] == 0xff;
        let units: Vec<_> = bytes[2..]
            .chunks_exact(2)
            .map(|pair| {
                if little {
                    u16::from_le_bytes([pair[0], pair[1]])
                } else {
                    u16::from_be_bytes([pair[0], pair[1]])
                }
            })
            .collect();
        String::from_utf16(&units).map_err(|_| invalid("信用证包含无效 UTF-16 文本。"))
    } else {
        String::from_utf8(
            bytes
                .strip_prefix(&[0xef, 0xbb, 0xbf])
                .unwrap_or(bytes)
                .to_vec(),
        )
        .map_err(|_| invalid("信用证文本须使用 UTF-8 或带 BOM 的 UTF-16 编码。"))
    }
}

fn ocr(service: &NativeService, source: &str, bytes: &[u8]) -> Result<String> {
    #[cfg(feature = "ocr")]
    {
        Ok(text(
            &super::ocr::recognize(service, bytes, source)?,
            "fullText",
        ))
    }
    #[cfg(not(feature = "ocr"))]
    {
        let _ = (service, source, bytes);
        Err(super::error::unsupported("当前组合未启用 OCR 能力。"))
    }
}
