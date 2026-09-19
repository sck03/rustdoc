//! OCR is an optional, isolated Rust worker. Source bytes use pipes only.
use super::{
    NativeService,
    error::{Result, error, invalid, unavailable},
    media, tasks,
};
use crate::{
    contracts, controlled_process,
    generated_api::*,
    paths::{self, RuntimePaths},
};
use serde_json::{Value, json};
use std::{path::PathBuf, process::Command, sync::TryLockError, time::Duration};
pub const OPERATIONS: &[Operation] = &[RECOGNIZE_OCR_IMAGE, UPLOAD_OCR_IMAGE, PREVIEW_OCR_IMAGE];
#[allow(dead_code)]
pub const LOCAL: &[Operation] = &[PREVIEW_OCR_IMAGE];
const MAX_INPUT: usize = 25 * 1024 * 1024;
pub struct Resources {
    pub worker: PathBuf,
    pub models: PathBuf,
    pub library: PathBuf,
}
impl Resources {
    pub fn new(paths: &RuntimePaths) -> Self {
        let tools = paths.app_root.join("sidecar").join("ocr");
        Self {
            worker: tools.join(if cfg!(windows) {
                "exportdoc-ocr.exe"
            } else {
                "exportdoc-ocr"
            }),
            models: paths
                .app_root
                .join("OcrModels")
                .join("PaddleOCR")
                .join("V6"),
            library: tools.join(if cfg!(windows) {
                "onnxruntime.dll"
            } else if cfg!(target_os = "macos") {
                "libonnxruntime.dylib"
            } else {
                "libonnxruntime.so"
            }),
        }
    }
    pub fn validate(&self) -> Result<()> {
        for path in [
            &self.worker,
            &self.library,
            &self.models.join("det").join("inference.onnx"),
            &self.models.join("rec").join("inference.onnx"),
            &self.models.join("rec").join("inference.yml"),
        ] {
            paths::ensure_safe_absolute(path).map_err(unavailable)?;
            match std::fs::metadata(path) {
                Ok(info) if info.is_file() && info.len() > 0 => {}
                Ok(_) => return Err(unavailable("OCR 资源不是有效文件。")),
                Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => {
                    return Err(unavailable(
                        "未安装完整 OCR 能力包，请补齐原生识别工具、ONNX Runtime 和 PP-OCRv6 模型。",
                    ));
                }
                Err(_) => return Err(unavailable("无法访问 OCR 资源。")),
            }
        }
        Ok(())
    }
    fn command(&self, paths: &RuntimePaths) -> Command {
        let mut command = Command::new(&self.worker);
        command
            .arg("--model-root")
            .arg(&self.models)
            .arg("--allowed-root")
            .arg(&paths.app_root)
            .current_dir(&paths.cache_root)
            .env("ORT_DYLIB_PATH", &self.library)
            .env("TEMP", &paths.cache_root)
            .env("TMP", &paths.cache_root)
            .env_remove("EXPORTDOCMANAGER_MASTER_KEY");
        command
    }
}
pub fn diagnostic(paths: &RuntimePaths) -> Value {
    let resources = Resources::new(paths);
    let availability = resources.validate();
    json!({"key":"ocr-runtime","label":"智能 OCR","requirement":"optional","status":if availability.is_ok(){"ready"}else{"missing"},"ready":availability.is_ok(),"resolvedPath":resources.worker,"message":availability.err().map(|e|e.message).unwrap_or_else(||"Rust 识别资源齐全；识别时检查模型加载和执行结果。".into())})
}
#[allow(dead_code)]
pub fn preview(_service: &NativeService, query: &[(&str, String)]) -> Result<tasks::FileOutput> {
    let selected = query
        .iter()
        .find(|(key, _)| *key == "filePath")
        .map(|(_, value)| value.as_str())
        .unwrap_or("");
    if selected.is_empty() {
        return Err(invalid("请提供本机图片路径。"));
    }
    let path = PathBuf::from(selected);
    let bytes = media::read_local(&path, MAX_INPUT)?;
    let mime = match path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
        .as_str()
    {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "bmp" => "image/bmp",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };
    Ok(tasks::FileOutput {
        file_name: path
            .file_name()
            .and_then(|name| name.to_str())
            .unwrap_or("ocr-image")
            .to_string(),
        media_type: mime.to_string(),
        content: bytes,
    })
}
pub fn local(service: &NativeService, body: &Value) -> Result<Value> {
    let path = PathBuf::from(super::records::text(body, "filePath"));
    let bytes = media::read_local(&path, MAX_INPUT)?;
    recognize(service, &bytes, &path.to_string_lossy())
}
pub fn recognize(service: &NativeService, bytes: &[u8], source: &str) -> Result<Value> {
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err(error(413, "OCR 图片必须非空且不能超过 25 MiB。"));
    }
    let resources = Resources::new(&service.paths);
    resources.validate()?;
    let _lease = service.ocr_gate.try_lock().map_err(|cause| match cause {
        TryLockError::WouldBlock => error(429, "OCR 正在处理其他图片，请稍后重试。"),
        TryLockError::Poisoned(_) => unavailable("OCR 执行状态异常。"),
    })?;
    let mut command = resources.command(&service.paths);
    command.arg("--recognize-stdin");
    let output = controlled_process::run(
        &mut command,
        bytes.to_vec(),
        4 * 1024 * 1024,
        Duration::from_secs(90),
    )?;
    if !output.status.success() {
        return Err(unavailable(format!(
            "OCR 工具未能完成识别：{}",
            output.stderr.trim()
        )));
    }
    let result: Value = serde_json::from_slice(&output.stdout)
        .map_err(|_| unavailable("OCR 工具返回了无效结果。"))?;
    if result["id"] != "stdin" {
        return Err(unavailable("OCR 响应标识不匹配。"));
    }
    if result["success"] != true {
        return Err(invalid(
            result["error"].as_str().unwrap_or("图片无法识别。"),
        ));
    }
    let full_text = result["fullText"]
        .as_str()
        .ok_or_else(|| unavailable("OCR 缺少识别文本。"))?;
    let lines = result["lines"]
        .as_array()
        .filter(|rows| rows.len() <= 2000)
        .ok_or_else(|| unavailable("OCR 识别行数超出范围。"))?;
    for row in lines {
        if row["text"].as_str().is_none_or(|s| s.len() > 64 * 1024)
            || ["x", "y", "width", "height"]
                .iter()
                .any(|key| row[*key].as_u64().is_none_or(|n| n > 16384))
        {
            return Err(unavailable("OCR 识别行格式无效。"));
        }
    }
    Ok(contracts::project(
        contracts::schema("ApiOcrRecognizeImageResponse"),
        json!({"sourcePath":source,"fullText":full_text,"lines":lines,"storagePolicy":"只在内存和受控 Rust 进程管道中处理图片；识别结果不写入数据库，不生成默认输出文件。"}),
    ))
}
