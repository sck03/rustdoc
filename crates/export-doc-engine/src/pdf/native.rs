//! PDFium's public C ABI, confined to a bounded decoder subprocess.
use libloading::Library;
use std::{
    ffi::{c_char, c_int, c_void},
    io::Cursor,
    marker::PhantomData,
    path::Path,
};

type Handle = *mut c_void;
type Close = unsafe extern "system" fn(Handle);
struct OwnedHandle {
    value: Handle,
    close: Close,
}
impl Drop for OwnedHandle {
    fn drop(&mut self) {
        unsafe { (self.close)(self.value) };
    }
}

pub struct Document<'a> {
    library: Library,
    document: Handle,
    close: Close,
    destroy: unsafe extern "system" fn(),
    bytes: PhantomData<&'a [u8]>,
}
impl Drop for Document<'_> {
    fn drop(&mut self) {
        unsafe {
            (self.close)(self.document);
            (self.destroy)();
        }
    }
}
macro_rules! symbol {
    ($library:expr, $name:literal, $signature:ty) => {
        *$library
            .get::<$signature>(concat!($name, "\0").as_bytes())
            .map_err(|e| e.to_string())?
    };
}
#[repr(C)]
struct FileWriter {
    version: c_int,
    write: unsafe extern "C" fn(*mut FileWriter, *const c_void, std::ffi::c_ulong) -> c_int,
    bytes: Vec<u8>,
    limit: usize,
}
unsafe extern "C" fn write_block(
    writer: *mut FileWriter,
    data: *const c_void,
    size: std::ffi::c_ulong,
) -> c_int {
    // PDFium calls synchronously with the FileWriter pointer supplied below.
    let writer = unsafe { &mut *writer };
    let size = size as usize;
    if writer
        .bytes
        .len()
        .checked_add(size)
        .is_none_or(|n| n > writer.limit)
        || writer.bytes.try_reserve(size).is_err()
    {
        return 0;
    }
    if size > 0 {
        writer
            .bytes
            .extend_from_slice(unsafe { std::slice::from_raw_parts(data.cast::<u8>(), size) });
    }
    1
}
pub(super) fn merge(files: &[Vec<u8>], path: &Path, limit: usize) -> Result<Vec<u8>, String> {
    let first = files.first().ok_or("请选择 PDF 文件。")?;
    let host = Document::open(first, path)?;
    let total_bytes: usize = files.iter().map(Vec::len).sum();
    unsafe {
        let create = symbol!(
            host.library,
            "FPDF_CreateNewDocument",
            unsafe extern "system" fn() -> Handle
        );
        let load = symbol!(
            host.library,
            "FPDF_LoadMemDocument64",
            unsafe extern "system" fn(*const c_void, usize, *const c_char) -> Handle
        );
        let import = symbol!(
            host.library,
            "FPDF_ImportPages",
            unsafe extern "system" fn(Handle, Handle, *const c_char, c_int) -> c_int
        );
        let page_count = symbol!(
            host.library,
            "FPDF_GetPageCount",
            unsafe extern "system" fn(Handle) -> c_int
        );
        let load_page = symbol!(
            host.library,
            "FPDF_LoadPage",
            unsafe extern "system" fn(Handle, c_int) -> Handle
        );
        let close_page = symbol!(host.library, "FPDF_ClosePage", Close);
        let output = OwnedHandle {
            value: create(),
            close: host.close,
        };
        if output.value.is_null() {
            return Err("无法创建合并 PDF。".into());
        }
        let mut pages = 0;
        let mut area = 0f64;
        for bytes in files {
            let source = OwnedHandle {
                value: load(bytes.as_ptr().cast(), bytes.len(), std::ptr::null()),
                close: host.close,
            };
            if source.value.is_null() {
                return Err("PDF 损坏、受密码保护或无法读取。".into());
            }
            let count = page_count(source.value);
            if !(1..=200).contains(&count) || pages + count > 400 {
                return Err("单个 PDF 最多 200 页，合并后最多 400 页。".into());
            }
            for index in 0..count {
                let page = OwnedHandle {
                    value: load_page(source.value, index),
                    close: close_page,
                };
                if page.value.is_null() {
                    return Err("无法读取 PDF 页面。".into());
                }
                let (w, h) = host.dimensions(page.value)?;
                let a = w * h;
                area += a;
                if w > 14400.
                    || h > 14400.
                    || a > 20_000_000.
                    || !area.is_finite()
                    || area > 250_000_000.
                {
                    return Err("PDF 页面尺寸或总面积超过限制。".into());
                }
                let estimate = total_bytes as f64 * 3.
                    + f64::from(pages + count) * 384. * 1024.
                    + (area / (595. * 842.)).ceil() * 64. * 1024.;
                if estimate > 512. * 1024. * 1024. {
                    return Err("PDF 合并预计内存超过 512 MiB，请分批处理。".into());
                }
            }
            if import(output.value, source.value, std::ptr::null(), pages) == 0 {
                return Err("PDF 页面合并失败。".into());
            }
            pages += count;
        }
        let mut writer = FileWriter {
            version: 1,
            write: write_block,
            bytes: vec![],
            limit,
        };
        let save = symbol!(
            host.library,
            "FPDF_SaveAsCopy",
            unsafe extern "system" fn(Handle, *mut FileWriter, std::ffi::c_ulong) -> c_int
        );
        if save(output.value, &mut writer, 0) == 0 {
            return Err("无法完成合并 PDF，输出容量上限为 64 MiB。".into());
        }
        Ok(writer.bytes)
    }
}
impl<'a> Document<'a> {
    pub fn open(bytes: &'a [u8], path: &Path) -> Result<Self, String> {
        crate::paths::ensure_safe_absolute(path)?;
        unsafe {
            let library = Library::new(path).map_err(|e| format!("随包 PDFium 不可用：{e}"))?;
            let initialize = symbol!(library, "FPDF_InitLibrary", unsafe extern "system" fn());
            let destroy = symbol!(library, "FPDF_DestroyLibrary", unsafe extern "system" fn());
            let load = symbol!(
                library,
                "FPDF_LoadMemDocument64",
                unsafe extern "system" fn(*const c_void, usize, *const c_char) -> Handle
            );
            let close = symbol!(library, "FPDF_CloseDocument", Close);
            initialize();
            let document = load(bytes.as_ptr().cast(), bytes.len(), std::ptr::null());
            if document.is_null() {
                destroy();
                return Err("PDF 已损坏、受密码保护或格式不受支持。".into());
            }
            Ok(Self {
                library,
                document,
                close,
                destroy,
                bytes: PhantomData,
            })
        }
    }
    pub fn count(&self) -> Result<u32, String> {
        let count = unsafe {
            symbol!(
                self.library,
                "FPDF_GetPageCount",
                unsafe extern "system" fn(Handle) -> c_int
            )(self.document)
        };
        if count <= 0 {
            return Err("PDF 没有可读取页面。".into());
        }
        Ok(count as u32)
    }
    fn page(&self, index: u32) -> Result<OwnedHandle, String> {
        if index >= self.count()? {
            return Err("PDF 页码超出范围。".into());
        }
        unsafe {
            let close = symbol!(self.library, "FPDF_ClosePage", Close);
            let value = symbol!(
                self.library,
                "FPDF_LoadPage",
                unsafe extern "system" fn(Handle, c_int) -> Handle
            )(self.document, index as i32);
            if value.is_null() {
                return Err("PDF 页面无法读取。".into());
            }
            Ok(OwnedHandle { value, close })
        }
    }
    fn dimensions(&self, page: Handle) -> Result<(f64, f64), String> {
        let (width, height) = unsafe {
            (
                symbol!(
                    self.library,
                    "FPDF_GetPageWidth",
                    unsafe extern "system" fn(Handle) -> f64
                )(page),
                symbol!(
                    self.library,
                    "FPDF_GetPageHeight",
                    unsafe extern "system" fn(Handle) -> f64
                )(page),
            )
        };
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
            let page = self.page(index)?;
            let (points_x, points_y) = self.dimensions(page.value)?;
            let (width, height) = Self::ocr_dimensions(points_x, points_y)?;
            result.raster_pixels += u64::from(width) * u64::from(height);
            if result.raster_pixels > 200_000_000 {
                return Err("信用证 PDF 总渲染像素超过 2 亿。".into());
            }
            unsafe {
                let close = symbol!(self.library, "FPDFText_ClosePage", Close);
                let value = symbol!(
                    self.library,
                    "FPDFText_LoadPage",
                    unsafe extern "system" fn(Handle) -> Handle
                )(page.value);
                if value.is_null() {
                    return Err("PDF 文本层无法读取。".into());
                }
                let text = OwnedHandle { value, close };
                let length = symbol!(
                    self.library,
                    "FPDFText_CountChars",
                    unsafe extern "system" fn(Handle) -> c_int
                )(text.value);
                if !(0..=500_000).contains(&length) {
                    return Err("PDF 文本超过允许长度。".into());
                }
                let mut buffer = vec![0_u16; length as usize + 1];
                let read = symbol!(
                    self.library,
                    "FPDFText_GetText",
                    unsafe extern "system" fn(Handle, c_int, c_int, *mut u16) -> c_int
                )(text.value, 0, length, buffer.as_mut_ptr());
                if read < 0 || read as usize > buffer.len() {
                    return Err("PDF 文本层返回无效长度。".into());
                }
                let read = (read as usize).saturating_sub(1);
                let content = String::from_utf16_lossy(&buffer[..read]);
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
        let page = self.page(index)?;
        let (points_x, points_y) = self.dimensions(page.value)?;
        let (width, height) = if for_ocr {
            Self::ocr_dimensions(points_x, points_y)?
        } else {
            let scale = (1200. / points_x).min(1800. / points_y).min(2.);
            (
                (points_x * scale).ceil() as u32,
                (points_y * scale).ceil() as u32,
            )
        };
        let length = width as usize * height as usize * 4;
        if length == 0
            || length
                > if for_ocr {
                    48_000_000
                } else {
                    super::PREVIEW_LIMIT as usize
                }
        {
            return Err("PDF 页面图像超出容量上限。".into());
        }
        let mut pixels = vec![255_u8; length];
        unsafe {
            let close = symbol!(self.library, "FPDFBitmap_Destroy", Close);
            let surface = symbol!(
                self.library,
                "FPDFBitmap_CreateEx",
                unsafe extern "system" fn(c_int, c_int, c_int, *mut c_void, c_int) -> Handle
            )(
                width as i32,
                height as i32,
                4,
                pixels.as_mut_ptr().cast(),
                (width * 4) as i32,
            );
            if surface.is_null() {
                return Err("无法分配 PDF 位图。".into());
            }
            let surface = OwnedHandle {
                value: surface,
                close,
            };
            symbol!(
                self.library,
                "FPDF_RenderPageBitmap",
                unsafe extern "system" fn(Handle, Handle, c_int, c_int, c_int, c_int, c_int, c_int)
            )(
                surface.value,
                page.value,
                0,
                0,
                width as i32,
                height as i32,
                0,
                1,
            );
        }
        for pixel in pixels.chunks_exact_mut(4) {
            pixel.swap(0, 2);
        }
        let bitmap =
            image::RgbaImage::from_raw(width, height, pixels).ok_or("PDF 位图尺寸无效。")?;
        let mut encoded = Cursor::new(Vec::new());
        bitmap
            .write_to(&mut encoded, image::ImageFormat::Png)
            .map_err(|e| e.to_string())?;
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
