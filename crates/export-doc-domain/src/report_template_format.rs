//! Project-owned report template container.
//!
//! `.dtpl` is deliberately not JSON. The V3 document remains the single AST,
//! but the file carries a magic marker, format version, payload length and
//! SHA-256 digest so plain text editors cannot silently rewrite a template.
use crate::designer::Design;
use sha2::{Digest, Sha256};
use std::io::{Cursor, Read, Write};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

pub const REPORT_TEMPLATE_EXTENSION: &str = ".dtpl";
const MAGIC: &[u8; 11] = b"EXPORTDOCDT";
const FORMAT_VERSION: u16 = 1;
const DOCUMENT_NAME: &str = "document.v3.json";
const MAX_DOCUMENT_BYTES: usize = 4 * 1024 * 1024;
const MAX_CONTAINER_BYTES: usize = 10 * 1024 * 1024;

pub fn encode(design: &Design) -> Result<Vec<u8>, String> {
    let document = serde_json::to_vec(design).map_err(|error| error.to_string())?;
    if document.is_empty() || document.len() > MAX_DOCUMENT_BYTES {
        return Err("报表模板结构超过允许的大小。".into());
    }
    let mut zip = ZipWriter::new(Cursor::new(Vec::new()));
    let options = SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated)
        .unix_permissions(0o600);
    zip.start_file(DOCUMENT_NAME, options)
        .map_err(|error| error.to_string())?;
    zip.write_all(&document)
        .map_err(|error| error.to_string())?;
    let payload = zip
        .finish()
        .map_err(|error| error.to_string())?
        .into_inner();
    let digest = Sha256::digest(&payload);
    let mut output = Vec::with_capacity(MAGIC.len() + 2 + 4 + digest.len() + payload.len());
    output.extend_from_slice(MAGIC);
    output.extend_from_slice(&FORMAT_VERSION.to_le_bytes());
    output.extend_from_slice(&(payload.len() as u32).to_le_bytes());
    output.extend_from_slice(&digest);
    output.extend_from_slice(&payload);
    Ok(output)
}

pub fn decode(bytes: &[u8]) -> Result<Design, String> {
    if bytes.len() < MAGIC.len() + 2 + 4 + 32 || bytes.len() > MAX_CONTAINER_BYTES {
        return Err("报表模板不是有效的 .dtpl 文件或超过允许的大小。".into());
    }
    if &bytes[..MAGIC.len()] != MAGIC {
        return Err("报表模板缺少 .dtpl 文件标记。".into());
    }
    let mut cursor = MAGIC.len();
    let version = u16::from_le_bytes(
        bytes[cursor..cursor + 2]
            .try_into()
            .map_err(|_| "报表模板格式头无效。")?,
    );
    cursor += 2;
    if version != FORMAT_VERSION {
        return Err("报表模板格式版本不受支持。".into());
    }
    let length = u32::from_le_bytes(
        bytes[cursor..cursor + 4]
            .try_into()
            .map_err(|_| "报表模板长度无效。")?,
    ) as usize;
    cursor += 4;
    let digest = &bytes[cursor..cursor + 32];
    cursor += 32;
    if length == 0
        || length > MAX_CONTAINER_BYTES
        || bytes.len() != cursor + length
        || Sha256::digest(&bytes[cursor..]).as_slice() != digest
    {
        return Err("报表模板内容损坏或校验失败。".into());
    }
    let payload = &bytes[cursor..];
    let mut archive = ZipArchive::new(Cursor::new(payload)).map_err(|_| "报表模板容器损坏。")?;
    if archive.len() != 1 {
        return Err("报表模板容器结构无效。".into());
    }
    let mut entry = archive.by_index(0).map_err(|_| "报表模板容器缺少正文。")?;
    if entry.name() != DOCUMENT_NAME || entry.size() as usize > MAX_DOCUMENT_BYTES {
        return Err("报表模板正文结构无效。".into());
    }
    let mut document = Vec::with_capacity(entry.size() as usize);
    entry
        .read_to_end(&mut document)
        .map_err(|_| "报表模板正文读取失败。")?;
    let source = std::str::from_utf8(&document).map_err(|_| "报表模板正文编码无效。")?;
    Design::from_source(source)
}
