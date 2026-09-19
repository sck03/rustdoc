//! Bounded local IO and bitmap decoding run on the application worker.
use std::{
    fs::File,
    io::{Cursor, Read, Write},
    path::Path,
};
pub struct ImageFrame {
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
}
pub fn read(path: &Path, limit: u64) -> Result<Vec<u8>, String> {
    export_doc_engine::paths::ensure_safe_absolute(path)?;
    let file = File::open(path).map_err(|cause| format!("无法打开所选文件：{cause}"))?;
    if !file
        .metadata()
        .map_err(|cause| cause.to_string())?
        .is_file()
    {
        return Err("请选择普通文件。".into());
    }
    let mut bytes = vec![];
    file.take(limit + 1)
        .read_to_end(&mut bytes)
        .map_err(|cause| cause.to_string())?;
    if bytes.len() as u64 > limit {
        return Err(format!("文件不能超过 {} MiB。", limit / 1024 / 1024));
    }
    export_doc_engine::operation::check().map_err(|cause| cause.to_string())?;
    Ok(bytes)
}
pub fn copy(source: &Path, destination: &Path) -> Result<(), String> {
    export_doc_engine::paths::ensure_safe_absolute(source)?;
    let mut input = File::open(source).map_err(|cause| cause.to_string())?;
    if !input
        .metadata()
        .map_err(|cause| cause.to_string())?
        .is_file()
    {
        return Err("备份来源必须是普通文件。".into());
    }
    export_doc_engine::paths::atomic_write_with(destination, |output| {
        let mut buffer = [0u8; 64 * 1024];
        loop {
            export_doc_engine::operation::check().map_err(|cause| cause.to_string())?;
            let count = input.read(&mut buffer).map_err(|cause| cause.to_string())?;
            if count == 0 {
                break;
            }
            output
                .write_all(&buffer[..count])
                .map_err(|cause| cause.to_string())?;
        }
        Ok(())
    })
}
pub fn image(bytes: &[u8]) -> Result<ImageFrame, String> {
    let mut reader = image::ImageReader::new(Cursor::new(bytes))
        .with_guessed_format()
        .map_err(|cause| cause.to_string())?;
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(8192);
    limits.max_image_height = Some(8192);
    limits.max_alloc = Some(128 * 1024 * 1024);
    reader.limits(limits);
    let bitmap = reader
        .decode()
        .map_err(|cause| format!("无法解码图片：{cause}"))?
        .thumbnail(1400, 1000)
        .into_rgba8();
    Ok(ImageFrame {
        width: bitmap.width(),
        height: bitmap.height(),
        rgba: bitmap.into_raw(),
    })
}
