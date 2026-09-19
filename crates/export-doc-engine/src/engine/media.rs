use super::error::{Result, error, invalid, unavailable};
use image::{ImageFormat, ImageReader, Limits};
use sha2::{Digest, Sha256};
use std::{
    io::{Cursor, Read},
    path::Path,
};

pub fn digest(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

pub fn image_type(bytes: &[u8], max_bytes: usize) -> Result<&'static str> {
    if bytes.is_empty() || bytes.len() > max_bytes {
        return Err(invalid("图片为空或超过容量上限。"));
    }
    let format = image::guess_format(bytes).map_err(|_| invalid("只支持 PNG 或 JPEG 图片。"))?;
    let content_type = match format {
        ImageFormat::Png => "image/png",
        ImageFormat::Jpeg => "image/jpeg",
        _ => return Err(invalid("只支持 PNG 或 JPEG 图片。")),
    };
    let mut reader = ImageReader::with_format(Cursor::new(bytes), format);
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
