mod credit;
mod merge;
pub(crate) use credit::extract_text;
mod native;
use crate::{api::ApiError, engine::error::unavailable, paths::ensure_safe_absolute};
pub use merge::{MAX_MERGE_INPUT, merge};
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
pub const PDF_LIMIT: u64 = 64 * 1024 * 1024;

pub(super) struct TextPage {
    pub text: String,
    pub needs_ocr: bool,
}

/// Both desktop and HTTP executables provide the same isolated decoder entry.
pub fn worker_args(args: &[String]) -> Option<Result<(), String>> {
    if args.iter().any(|a| a == "--pdf-merge-worker") {
        return Some(merge::worker(args));
    }
    if args.iter().any(|a| a == "--pdf-credit-worker") {
        return Some(credit::worker(args));
    }
    if !args.iter().any(|a| a == "--pdf-preview-worker") {
        return None;
    }
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
        worker(library, index)
    })())
}

/// Use the existing backend bundle's PDFium solely for native preview.
/// A bounded child process contains third-party decoder faults and cancellation.
pub fn render_page(
    bytes: &[u8],
    page_index: u32,
    pdfium_path: &Path,
) -> Result<PdfPageImage, ApiError> {
    ensure_safe_absolute(pdfium_path).map_err(unavailable)?;
    let mut command =
        Command::new(std::env::current_exe().map_err(|error| unavailable(error.to_string()))?);
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
    )?;
    if !output.status.success() {
        return Err(unavailable(format!(
            "PDF 预览进程失败（{}）：{}。仍可另存原 PDF。",
            output.status, output.stderr
        )));
    }
    let bytes = output.stdout;
    if bytes.len() < 16 || bytes.len() as u64 > PREVIEW_LIMIT + 16 {
        return Err(unavailable("PDF 预览响应超出容量范围。"));
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

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preview_preserves_request_deadlines_and_cancellation() {
        let library = std::env::current_exe().unwrap();
        for cancelled in [false, true] {
            let scope = crate::operation::OperationScope::new(if cancelled {
                Duration::from_secs(60)
            } else {
                Duration::ZERO
            });
            if cancelled {
                scope.cancel();
            }
            let error = scope
                .run(|| render_page(b"%PDF-test", 0, &library))
                .err()
                .unwrap();
            assert_eq!(error.status, Some(504));
            assert_eq!(error.diagnostic, Some("cancelled-or-timeout"));
        }
    }
}

/// No API or file writes: the parent owns the decoder process deadline.
pub fn worker(pdfium_path: &Path, page_index: u32) -> Result<(), String> {
    ensure_safe_absolute(pdfium_path)?;
    let mut pdf = vec![];
    std::io::stdin()
        .take(PDF_LIMIT + 1)
        .read_to_end(&mut pdf)
        .map_err(|error| error.to_string())?;
    if pdf.len() as u64 > PDF_LIMIT || !pdf.starts_with(b"%PDF-") {
        return Err("无效或超限的 PDF 输入。".into());
    }
    let page = native::with_document(&pdf, pdfium_path, |document| {
        document.render(page_index, false)
    })?;
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
