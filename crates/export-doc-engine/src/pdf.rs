mod merge;
mod native;
use crate::paths::ensure_safe_absolute;
pub use merge::{MAX_MERGE_INPUT, merge};
use serde::{Deserialize, Serialize};
use std::{
    io::{Read, Write},
    path::Path,
    process::Command,
    time::Duration,
};

#[derive(Clone)]
pub struct PdfPageImage {
    pub png: Vec<u8>,
    pub width: u32,
    pub height: u32,
    pub page_count: u32,
    pub page_index: u32,
}
pub(super) const PREVIEW_LIMIT: u64 = 16 * 1024 * 1024;

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TextDocument {
    pub page_count: u32,
    pub text: String,
    pub raster_pixels: u64,
}

fn decoder(
    bytes: &[u8],
    library: &Path,
    mode: &str,
    index: u32,
    limit: usize,
) -> Result<Vec<u8>, String> {
    ensure_safe_absolute(library)?;
    let mut command = Command::new(std::env::current_exe().map_err(|e| e.to_string())?);
    command
        .arg(mode)
        .arg("--pdfium")
        .arg(library)
        .arg("--page")
        .arg(index.to_string());
    let output = crate::controlled_process::run(
        &mut command,
        bytes.to_vec(),
        limit,
        Duration::from_secs(60),
    )
    .map_err(|e| e.to_string())?;
    if !output.status.success() {
        return Err(format!("PDF 处理失败：{}", output.stderr.trim()));
    }
    Ok(output.stdout)
}
pub fn extract_text(bytes: &[u8], library: &Path) -> Result<TextDocument, String> {
    serde_json::from_slice(&decoder(
        bytes,
        library,
        "--pdf-text-worker",
        0,
        4 * 1024 * 1024,
    )?)
    .map_err(|_| "PDF 文字响应无效。".into())
}
pub fn ocr_page(bytes: &[u8], index: u32, library: &Path) -> Result<Vec<u8>, String> {
    decoder(
        bytes,
        library,
        "--pdf-ocr-page-worker",
        index,
        25 * 1024 * 1024,
    )
}
/// Both desktop and HTTP executables provide the same isolated decoder entry.
pub fn worker_args(args: &[String]) -> Option<Result<(), String>> {
    if args.iter().any(|a| a == "--pdf-merge-worker") {
        return Some(merge::worker(args));
    }
    let mode = args.iter().find(|a| {
        [
            "--pdf-preview-worker",
            "--pdf-text-worker",
            "--pdf-ocr-page-worker",
        ]
        .contains(&a.as_str())
    })?;
    Some((|| {
        let option = |key| {
            args.iter()
                .position(|a| a == key)
                .and_then(|i| args.get(i + 1))
        };
        let library = Path::new(option("--pdfium").ok_or("缺少 PDFium 路径。")?);
        let index = option("--page")
            .and_then(|s| s.parse().ok())
            .ok_or("缺少 PDF 页码。")?;
        if mode == "--pdf-preview-worker" {
            return worker(library, index);
        }
        let mut bytes = Vec::new();
        std::io::stdin()
            .take(25 * 1024 * 1024 + 1)
            .read_to_end(&mut bytes)
            .map_err(|e| e.to_string())?;
        if bytes.len() > 25 * 1024 * 1024 || !bytes.starts_with(b"%PDF-") {
            return Err("无效或超限的 PDF 输入。".into());
        }
        let document = native::Document::open(&bytes, library)?;
        let output = if mode == "--pdf-text-worker" {
            serde_json::to_vec(&document.extract()?).map_err(|e| e.to_string())?
        } else {
            document.render(index, true)?.png
        };
        std::io::stdout()
            .write_all(&output)
            .and_then(|_| std::io::stdout().flush())
            .map_err(|e| e.to_string())
    })())
}

/// Use the existing backend bundle's PDFium solely for native preview.
/// A bounded child process contains third-party decoder faults and cancellation.
pub fn render_page(
    bytes: &[u8],
    page_index: u32,
    pdfium_path: &Path,
) -> Result<PdfPageImage, String> {
    ensure_safe_absolute(pdfium_path)?;
    let mut command = Command::new(std::env::current_exe().map_err(|error| error.to_string())?);
    command
        .arg("--pdf-preview-worker")
        .arg("--pdfium")
        .arg(pdfium_path)
        .arg("--page")
        .arg(page_index.to_string());
    let output = crate::controlled_process::run(
        &mut command,
        bytes.to_vec(),
        PREVIEW_LIMIT as usize + 16,
        Duration::from_secs(30),
    )
    .map_err(|e| e.to_string())?;
    if !output.status.success() {
        return Err(format!(
            "PDF 预览进程失败（{}）：{}。仍可另存原 PDF。",
            output.status, output.stderr
        ));
    }
    let bytes = output.stdout;
    if bytes.len() < 16 || bytes.len() as u64 > PREVIEW_LIMIT + 16 {
        return Err("PDF 预览响应超出容量范围。".into());
    }
    let read = |start| u32::from_le_bytes(bytes[start..start + 4].try_into().unwrap());
    Ok(PdfPageImage {
        width: read(0),
        height: read(4),
        page_count: read(8),
        page_index: read(12),
        png: bytes[16..].to_vec(),
    })
}

/// No API or file writes: the parent owns the decoder process deadline.
pub fn worker(pdfium_path: &Path, page_index: u32) -> Result<(), String> {
    ensure_safe_absolute(pdfium_path)?;
    let mut pdf = vec![];
    std::io::stdin()
        .take(crate::jobs::PDF_LIMIT + 1)
        .read_to_end(&mut pdf)
        .map_err(|error| error.to_string())?;
    if pdf.len() as u64 > crate::jobs::PDF_LIMIT || !pdf.starts_with(b"%PDF-") {
        return Err("无效或超限的 PDF 输入。".into());
    }
    let page = native::Document::open(&pdf, pdfium_path)?.render(page_index, false)?;
    let mut stdout = std::io::stdout().lock();
    for value in [page.width, page.height, page.page_count, page.page_index] {
        stdout
            .write_all(&value.to_le_bytes())
            .map_err(|error| error.to_string())?;
    }
    stdout
        .write_all(&page.png)
        .and_then(|_| stdout.flush())
        .map_err(|error| error.to_string())
}
