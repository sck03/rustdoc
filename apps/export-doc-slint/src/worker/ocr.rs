use super::files::{self, ImageFrame};
use std::{io::Cursor, path::PathBuf, sync::Arc};
pub enum Source {
    File(PathBuf),
    Clipboard(ImageFrame),
}
pub struct Image {
    pub name: String,
    pub bytes: Arc<Vec<u8>>,
    pub preview: ImageFrame,
    pub width: u32,
    pub height: u32,
}
pub fn load(source: Source) -> Result<Image, String> {
    let (name, bytes) = match source {
        Source::File(path) => {
            let bytes = files::read(&path, 25 * 1024 * 1024)?;
            let name = path
                .file_name()
                .and_then(|s| s.to_str())
                .ok_or("图片名称无效。")?
                .to_owned();
            (name, bytes)
        }
        Source::Clipboard(frame) => {
            let image = image::RgbaImage::from_raw(frame.width, frame.height, frame.rgba)
                .ok_or("剪贴板图片尺寸无效。")?;
            let mut data = Cursor::new(Vec::new());
            image
                .write_to(&mut data, image::ImageFormat::Png)
                .map_err(|e| e.to_string())?;
            ("剪贴板图片.png".into(), data.into_inner())
        }
    };
    if bytes.is_empty() || bytes.len() > 25 * 1024 * 1024 {
        return Err("OCR 图片必须非空且不能超过 25 MiB。".into());
    }
    let (width, height) = image::ImageReader::new(Cursor::new(&bytes))
        .with_guessed_format()
        .map_err(|e| e.to_string())?
        .into_dimensions()
        .map_err(|e| e.to_string())?;
    if width == 0
        || height == 0
        || width > 16384
        || height > 16384
        || u64::from(width) * u64::from(height) > 40_000_000
    {
        return Err("OCR 图片单边不得超过 16384 像素，总计不得超过 4000 万像素。".into());
    }
    let mut reader = image::ImageReader::new(Cursor::new(&bytes))
        .with_guessed_format()
        .map_err(|e| e.to_string())?;
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(16384);
    limits.max_image_height = Some(16384);
    limits.max_alloc = Some(192 * 1024 * 1024);
    reader.limits(limits);
    let bitmap = reader
        .decode()
        .map_err(|e| e.to_string())?
        .thumbnail(1400, 1200)
        .into_rgba8();
    export_doc_engine::operation::check().map_err(|e| e.to_string())?;
    let preview = ImageFrame {
        width: bitmap.width(),
        height: bitmap.height(),
        rgba: bitmap.into_raw(),
    };
    Ok(Image {
        name,
        bytes: Arc::new(bytes),
        preview,
        width,
        height,
    })
}
