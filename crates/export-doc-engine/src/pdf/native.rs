//! PDFium access confined to a bounded decoder subprocess.
//!
//! `pdfium-render` owns the C ABI and object lifetime. This module keeps the
//! project-specific path validation, capacity limits and error wording so the
//! HTTP/Tauri callers and worker protocol remain unchanged.
use pdfium_render::prelude::*;
use std::{
    io::{Cursor, Write},
    path::Path,
};

#[derive(Default)]
struct LimitedWriter {
    bytes: Vec<u8>,
    limit: usize,
}

impl Write for LimitedWriter {
    fn write(&mut self, buffer: &[u8]) -> std::io::Result<usize> {
        let size = buffer.len();
        if self
            .bytes
            .len()
            .checked_add(size)
            .is_none_or(|total| total > self.limit)
        {
            return Err(std::io::Error::other("PDF 输出超过容量上限。"));
        }
        self.bytes.extend_from_slice(buffer);
        Ok(size)
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

struct BoundPdfium {
    bindings: Box<dyn PdfiumLibraryBindings>,
}

impl BoundPdfium {
    fn open(path: &Path) -> Result<Self, String> {
        crate::paths::ensure_safe_absolute(path)?;
        if !path.is_file() {
            return Err(format!("随包 PDFium 不可用:{}", path.display()));
        }
        let bindings = Pdfium::bind_to_library(path)
            .map_err(|error| format!("随包 PDFium 不可用({}):{error}", path.display()))?;
        Ok(Self { bindings })
    }

    fn into_pdfium(self) -> Pdfium {
        Pdfium::new(self.bindings)
    }
}

pub struct Document {
    pdfium: Box<Pdfium>,
    bytes: Vec<u8>,
}

pub(super) fn merge(files: &[Vec<u8>], path: &Path, limit: usize) -> Result<Vec<u8>, String> {
    let bound = BoundPdfium::open(path)?;
    let total_bytes: usize = files.iter().map(Vec::len).sum();
    let pdfium = bound.into_pdfium();
    let mut output = pdfium
        .create_new_pdf()
        .map_err(|_| "无法创建合并 PDF。".to_string())?;
    let mut pages = 0usize;
    let mut area = 0f64;
    for bytes in files {
        let source = pdfium
            .load_pdf_from_byte_slice(bytes, None)
            .map_err(|_| "PDF 已损坏、受密码保护或格式不受支持。".to_string())?;
        let count = source.pages().len() as usize;
        if !(1..=200).contains(&count) || pages + count > 400 {
            return Err("单个 PDF 最多 200 页,合并后最多 400 页。".into());
        }
        for page in source.pages().iter() {
            let width = f64::from(page.width().value);
            let height = f64::from(page.height().value);
            let page_area = width * height;
            area += page_area;
            if width > 14_400.
                || height > 14_400.
                || page_area > 20_000_000.
                || !area.is_finite()
                || area > 250_000_000.
            {
                return Err("PDF 页面尺寸或总面积超过限制。".into());
            }
            let estimate = total_bytes as f64 * 3.
                + (pages + count) as f64 * 384. * 1024.
                + (area / (595. * 842.)).ceil() * 64. * 1024.;
            if estimate > 512. * 1024. * 1024. {
                return Err("PDF 合并预计内存超过 512 MiB,请分批处理。".into());
            }
        }
        output
            .pages_mut()
            .append(&source)
            .map_err(|_| "PDF 页面合并失败。".to_string())?;
        pages += count;
    }
    let mut writer = LimitedWriter {
        bytes: Vec::new(),
        limit,
    };
    output
        .save_to_writer(&mut writer)
        .map_err(|_| "无法完成合并 PDF,输出容量上限为 64 MiB。".to_string())?;
    let bytes = writer.bytes;
    if !bytes.starts_with(b"%PDF-") {
        return Err("PDF 合并未生成有效文件。".into());
    }
    Ok(bytes)
}

impl Document {
    pub fn open(bytes: &[u8], path: &Path) -> Result<Self, String> {
        let pdfium = Box::new(BoundPdfium::open(path)?.into_pdfium());
        pdfium
            .load_pdf_from_byte_slice(bytes, None)
            .map_err(|_| "PDF 已损坏、受密码保护或格式不受支持。".to_string())?;
        Ok(Self {
            pdfium,
            bytes: bytes.to_vec(),
        })
    }

    fn document(&self) -> Result<PdfDocument<'_>, String> {
        self.pdfium
            .load_pdf_from_byte_slice(&self.bytes, None)
            .map_err(|_| "PDF 已损坏、受密码保护或格式不受支持。".into())
    }

    pub fn count(&self) -> Result<u32, String> {
        let document = self.document()?;
        let count = document.pages().len() as u32;
        if count <= 0 {
            return Err("PDF 没有可读取页面。".into());
        }
        Ok(count)
    }

    fn dimensions(page: &PdfPage<'_>) -> Result<(f32, f32), String> {
        let width = page.width().value;
        let height = page.height().value;
        if !width.is_finite() || !height.is_finite() || width <= 0. || height <= 0. {
            return Err("PDF 页面尺寸无效。".into());
        }
        Ok((width, height))
    }

    pub fn extract(&self) -> Result<super::TextDocument, String> {
        let count = self.count()?;
        if count > 50 {
            return Err("信用证 PDF 最多 50 页。".into());
        }
        let mut result = super::TextDocument {
            page_count: count,
            text: String::new(),
            raster_pixels: 0,
        };
        for index in 0..count {
            let document = self.document()?;
            let pages = document.pages();
            if index >= pages.len() as u32 {
                return Err("PDF 页码超出范围。".into());
            }
            let page = pages
                .get(index as PdfPageIndex)
                .map_err(|_| "PDF 页面无法读取。".to_string())?;
            let (points_x, points_y) = Self::dimensions(&page)?;
            let (width, height) = Self::ocr_dimensions(points_x as f64, points_y as f64)?;
            result.raster_pixels += u64::from(width) * u64::from(height);
            if result.raster_pixels > 200_000_000 {
                return Err("信用证 PDF 总渲染像素超过 2 亿。".into());
            }
            let text_page = page
                .text()
                .map_err(|_| "PDF 文本层无法读取。".to_string())?;
            if text_page.chars().len() > 500_000 {
                return Err("PDF 文本超过允许长度。".into());
            }
            let content = text_page.all();
            if !content.trim().is_empty() {
                if !result.text.is_empty() {
                    result.text.push_str("\n\n");
                }
                result.text.push_str(content.trim());
            }
            if result.text.chars().count() > 500_000 {
                return Err("信用证文本超过 50 万字。".into());
            }
        }
        Ok(result)
    }

    fn ocr_dimensions(points_x: f64, points_y: f64) -> Result<(u32, u32), String> {
        let (width, height) = (
            (points_x * 200. / 72.).ceil(),
            (points_y * 200. / 72.).ceil(),
        );
        if width > 10_000. || height > 10_000. || width * height > 12_000_000. {
            return Err("信用证 PDF 页面渲染尺寸超过安全限制。".into());
        }
        Ok((width as u32, height as u32))
    }

    pub fn render(&self, index: u32, for_ocr: bool) -> Result<super::PdfPageImage, String> {
        let document = self.document()?;
        let pages = document.pages();
        if index >= pages.len() as u32 {
            return Err("PDF 页码超出范围。".into());
        }
        let page = pages
            .get(index as PdfPageIndex)
            .map_err(|_| "PDF 页面无法读取。".to_string())?;
        let (points_x, points_y) = Self::dimensions(&page)?;
        let (width, height) = if for_ocr {
            Self::ocr_dimensions(points_x as f64, points_y as f64)?
        } else {
            let scale = (1200. / points_x).min(1800. / points_y).min(2.);
            (
                (points_x * scale).ceil() as u32,
                (points_y * scale).ceil() as u32,
            )
        };
        let byte_limit = if for_ocr {
            48_000_000usize
        } else {
            super::PREVIEW_LIMIT as usize
        };
        let estimated_bytes = width as usize * height as usize * 4;
        if estimated_bytes == 0 || estimated_bytes > byte_limit {
            return Err("PDF 页面图像超出容量上限。".into());
        }
        let bitmap = page
            .render_with_config(
                &PdfRenderConfig::new().set_target_size(width as i32, height as i32),
            )
            .map_err(|_| "PDF 页面渲染失败。".to_string())?;
        let canvas = bitmap
            .as_image()
            .map_err(|_| "无法创建 PDF 位图。".to_string())?;
        let width = bitmap.width() as u32;
        let height = bitmap.height() as u32;
        let mut encoded = Cursor::new(Vec::new());
        canvas
            .write_to(&mut encoded, image::ImageFormat::Png)
            .map_err(|error| error.to_string())?;
        if encoded.get_ref().len() > 25 * 1024 * 1024 {
            return Err("PDF 页面编码超过图片容量上限。".into());
        }
        Ok(super::PdfPageImage {
            png: encoded.into_inner(),
            width,
            height,
            page_count: self.count()?,
            page_index: index,
        })
    }
}
