//! A task owns one PDF document and, only when needed, one OCR model process.
use super::native;
use crate::{
    api::ApiError,
    engine::error::{invalid, unavailable},
    paths::{RuntimePaths, ensure_safe_absolute},
};
use serde::{Deserialize, Serialize};
use std::{
    io::{Read, Write},
    path::Path,
    process::Command,
    time::Duration,
};

#[derive(Serialize, Deserialize)]
struct Failure {
    status: u16,
    message: String,
}
type Reply = Result<String, Failure>;

pub(crate) fn extract_text(bytes: &[u8], paths: &RuntimePaths) -> Result<String, ApiError> {
    ensure_safe_absolute(&paths.pdfium_path()).map_err(unavailable)?;
    let mut command =
        Command::new(std::env::current_exe().map_err(|e| unavailable(e.to_string()))?);
    command
        .arg("--pdf-credit-worker")
        .arg("--pdfium")
        .arg(paths.pdfium_path())
        .arg("--ocr-root")
        .arg(&paths.app_root)
        .arg("--cache-root")
        .arg(&paths.cache_root);
    let output = crate::controlled_process::run(
        &mut command,
        bytes,
        4 * 1024 * 1024,
        Duration::from_secs(590),
    )?;
    if !output.status.success() {
        return Err(unavailable("PDF 提取进程未能完成。"));
    }
    decode(&output.stdout)
}

fn decode(bytes: &[u8]) -> Result<String, ApiError> {
    let reply: Reply =
        serde_json::from_slice(bytes).map_err(|_| unavailable("PDF 提取响应无效。"))?;
    reply.map_err(|failure| ApiError {
        status: Some(match failure.status {
            400 | 413 | 501 | 503 | 504 => failure.status,
            _ => 503,
        }),
        message: failure.message,
        code: None,
        diagnostic: Some("pdf-credit-worker"),
    })
}

pub(super) fn worker(args: &[String]) -> Result<(), String> {
    let option = |key| {
        args.iter()
            .position(|a| a == key)
            .and_then(|i| args.get(i + 1))
            .map(Path::new)
            .ok_or_else(|| format!("缺少工作进程参数 {key}。"))
    };
    let library = option("--pdfium")?;
    let root = option("--ocr-root")?;
    let cache = option("--cache-root")?;
    let result = (|| {
        ensure_safe_absolute(root).map_err(unavailable)?;
        ensure_safe_absolute(cache).map_err(unavailable)?;
        let mut bytes = Vec::new();
        std::io::stdin()
            .take(25 * 1024 * 1024 + 1)
            .read_to_end(&mut bytes)
            .map_err(|e| unavailable(e.to_string()))?;
        if bytes.len() > 25 * 1024 * 1024 || !bytes.starts_with(b"%PDF-") {
            return Err(invalid("无效或超限的 PDF 输入。"));
        }
        native::with_document_result(&bytes, library, |document| {
            let pages = document.extract().map_err(invalid)?;
            let mut ocr = TaskOcr::new(root, cache);
            let mut output = String::new();
            let mut characters = 0;
            for (index, page) in pages.into_iter().enumerate() {
                let text = if page.needs_ocr {
                    let image = document.render(index as u32, true).map_err(invalid)?;
                    merge_page(&page.text, &ocr.recognize(image.png)?)
                } else {
                    page.text
                };
                if !text.trim().is_empty() {
                    characters += text.chars().count() + if output.is_empty() { 0 } else { 2 };
                    if characters > 500_000 {
                        return Err(invalid("信用证提取文本超过 50 万字，请拆分文件。"));
                    }
                    if !output.is_empty() {
                        output.push_str("\n\n");
                    }
                    output.push_str(&text);
                }
            }
            Ok(output)
        })
    })();
    let reply: Reply = result.map_err(|error: ApiError| Failure {
        status: error.status.unwrap_or(503),
        message: error.message,
    });
    let mut output = std::io::stdout().lock();
    serde_json::to_writer(&mut output, &reply).map_err(|e| e.to_string())?;
    output.flush().map_err(|e| e.to_string())
}

// Preserve native content that OCR missed, while avoiding repeated native lines.
fn merge_page(native: &str, recognized: &str) -> String {
    let normalized = |text: &str| text.split_whitespace().collect::<String>();
    let recognized_key = normalized(recognized);
    let mut text = recognized.trim().to_owned();
    for line in native
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
    {
        if !recognized_key.contains(&normalized(line)) {
            if !text.is_empty() {
                text.push('\n');
            }
            text.push_str(line);
        }
    }
    text
}

struct TaskOcr<'a> {
    root: &'a Path,
    cache: &'a Path,
    #[cfg(feature = "ocr")]
    process: Option<crate::controlled_process::FramedProcess>,
}
impl<'a> TaskOcr<'a> {
    fn new(root: &'a Path, cache: &'a Path) -> Self {
        Self {
            root,
            cache,
            #[cfg(feature = "ocr")]
            process: None,
        }
    }
    fn recognize(&mut self, image: Vec<u8>) -> Result<String, ApiError> {
        #[cfg(feature = "ocr")]
        {
            use crate::{controlled_process::FramedProcess, engine::ocr};
            if self.process.is_none() {
                let resources = ocr::Resources::from_root(self.root);
                resources.validate()?;
                let mut command = resources.command(self.cache);
                command.arg("--recognize-frames");
                self.process = Some(FramedProcess::start(
                    &mut command,
                    4 * 1024 * 1024,
                    Duration::from_secs(580),
                )?);
            }
            let bytes = self
                .process
                .as_mut()
                .ok_or_else(|| unavailable("OCR 进程未启动。"))?
                .exchange(image)?;
            let response = ocr::decode_response(&bytes, "PDF")?;
            Ok(response["fullText"].as_str().unwrap_or("").to_owned())
        }
        #[cfg(not(feature = "ocr"))]
        {
            let _ = (image, self.root, self.cache);
            Err(crate::engine::error::unsupported(
                "当前组合未启用 OCR 能力。",
            ))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn text_layers_are_preserved_without_duplicating_recognized_lines() {
        assert_eq!(
            merge_page("Native amount 100\nHeader", "Header\nScanned terms"),
            "Header\nScanned terms\nNative amount 100"
        );
        assert_eq!(merge_page("Header", ""), "Header");
    }
    #[test]
    fn extraction_errors_keep_their_category() {
        for status in [400, 413, 501, 503, 504] {
            let reply: Reply = Err(Failure {
                status,
                message: "failure".into(),
            });
            assert_eq!(
                decode(&serde_json::to_vec(&reply).unwrap())
                    .unwrap_err()
                    .status,
                Some(status)
            );
        }
    }
}
