use super::error::{Result, error, invalid, unavailable};
use image::{ImageFormat, ImageReader, Limits};
use sha2::{Digest, Sha256};
use std::{
    io::{BufRead, Cursor, Read, Seek, SeekFrom},
    path::Path,
};
use unicode_normalization::UnicodeNormalization;

pub const DOCUMENT_LIMIT: usize = super::file_store::policy::MAX_UPLOAD_BYTES as usize;
pub const SEAL_LIMIT: usize = 5 * 1024 * 1024;

pub fn read_seal_image(path: &Path) -> Result<Vec<u8>> {
    let bytes = read_local(path, SEAL_LIMIT)?;
    let media = image_type(&bytes, SEAL_LIMIT)?;
    let extension = path
        .extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if !match media {
        "image/png" => extension == "png",
        _ => matches!(extension.as_str(), "jpg" | "jpeg"),
    } {
        return Err(invalid("印章图片扩展名与实际图片格式不一致。"));
    }
    Ok(bytes)
}

/// Shared evidence-file validation for personnel and office requests.
pub(super) fn document_reader(
    file_name: &str,
    input: &mut (impl BufRead + Seek),
    size: u64,
) -> Result<(String, &'static str)> {
    let name = file_name.nfc().collect::<String>();
    if !crate::paths::valid_file_name(&name) || size == 0 || size > DOCUMENT_LIMIT as u64 {
        return Err(invalid("文件名无效，或文件为空、超过 100 MiB。"));
    }
    let extension = Path::new(&name)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if matches!(extension.as_str(), "doc" | "docx" | "xls" | "xlsx") {
        return Ok((
            name,
            super::document_formats::office_type(&extension, input)?,
        ));
    }
    if extension == "xml" {
        super::electronic_evidence::xml_reader(input)?;
        return Ok((name, "application/xml"));
    }
    if extension == "ofd" {
        super::electronic_evidence::ofd_reader(input)?;
        return Ok((name, "application/ofd"));
    }
    let mut header = [0; 16];
    let count = input.read(&mut header)?;
    let pdf = if header[..count].starts_with(b"%PDF-") {
        input.seek(SeekFrom::Start(size.saturating_sub(1024)))?;
        let mut tail = Vec::with_capacity(1024);
        input.take(1024).read_to_end(&mut tail)?;
        tail.windows(5).any(|part| part == b"%%EOF")
    } else {
        false
    };
    input.seek(SeekFrom::Start(0))?;
    let media_type = if pdf {
        "application/pdf"
    } else {
        image_reader(input, &header[..count])?
    };
    if !match media_type {
        "application/pdf" => extension == "pdf",
        "image/png" => extension == "png",
        _ => matches!(extension.as_str(), "jpg" | "jpeg"),
    } {
        return Err(invalid(
            "附件扩展名与实际类型不一致，只支持 PDF、PNG、JPEG、Word、Excel、OFD 和 XML。",
        ));
    }
    Ok((name, media_type))
}

pub fn digest(bytes: &[u8]) -> String {
    crate::paths::hex(&Sha256::digest(bytes))
}

pub fn image_type(bytes: &[u8], max_bytes: usize) -> Result<&'static str> {
    if bytes.is_empty() || bytes.len() > max_bytes {
        return Err(invalid("图片为空或超过容量上限。"));
    }
    image_reader(&mut Cursor::new(bytes), bytes)
}
fn image_reader(input: &mut (impl BufRead + Seek), header: &[u8]) -> Result<&'static str> {
    let format = image::guess_format(header).map_err(|_| invalid("只支持 PNG 或 JPEG 图片。"))?;
    let content_type = match format {
        ImageFormat::Png => "image/png",
        ImageFormat::Jpeg => "image/jpeg",
        _ => return Err(invalid("只支持 PNG 或 JPEG 图片。")),
    };
    let (width, height) = ImageReader::with_format(&mut *input, format)
        .into_dimensions()
        .map_err(|_| invalid("图片尺寸无效。"))?;
    if width == 0
        || height == 0
        || width > 8192
        || height > 8192
        || u64::from(width) * u64::from(height) > 32_000_000
    {
        return Err(invalid("图片尺寸或总像素超过限制。"));
    }
    input.seek(SeekFrom::Start(0))?;
    let mut reader = ImageReader::with_format(input, format);
    let mut limits = Limits::default();
    limits.max_image_width = Some(8192);
    limits.max_image_height = Some(8192);
    limits.max_alloc = Some(160 * 1024 * 1024);
    reader.limits(limits);
    let decoded = reader
        .decode()
        .map_err(|_| invalid("图片损坏或解码尺寸超出限制。"))?;
    if u64::from(decoded.width()) * u64::from(decoded.height()) > 32_000_000 {
        return Err(invalid("图片总像素超过 3200 万。"));
    }
    Ok(content_type)
}

pub fn read_local(path: &Path, limit: usize) -> Result<Vec<u8>> {
    crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
    let file = std::fs::File::open(path).map_err(|cause| {
        if cause.kind() == std::io::ErrorKind::NotFound {
            error(404, "选择的源文件不存在。")
        } else {
            unavailable(format!("源文件不可读：{cause}"))
        }
    })?;
    if !file.metadata()?.is_file() || file.metadata()?.len() > limit as u64 {
        return Err(invalid("源文件类型或容量不符合要求。"));
    }
    let mut bytes = vec![];
    file.take(limit as u64 + 1).read_to_end(&mut bytes)?;
    if bytes.len() > limit {
        return Err(error(413, "源文件超过容量上限。"));
    }
    crate::operation::check()?;
    Ok(bytes)
}
