//! Validate this Rust package's native resources before opening the UI. OS and
//! WebView2 checks run earlier, before Tauri itself initializes on Windows.
use crate::runtime_paths::RuntimePaths;
use export_doc_engine::paths::ensure_safe_absolute;
use std::{fs, path::Path};

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct Package {
    schema_version: u32,
    backend: String,
    frontend: String,
    ocr: bool,
}

pub(crate) fn check(paths: &RuntimePaths) -> Result<(), String> {
    let marker = paths.app_root.join("exportdoc-native-package.json");
    ensure_safe_absolute(&marker)?;
    let metadata = match fs::metadata(&marker) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            if cfg!(debug_assertions) {
                return Ok(());
            }
            return Err("程序包缺少 Rust 资源清单，请完整解压或重新安装。".into());
        }
        Err(error) => return Err(format!("无法读取运行资源清单：{error}")),
    };
    if !metadata.is_file() || metadata.len() > 16384 {
        return Err("运行资源清单超过大小限制。".into());
    }
    let bytes = fs::read(&marker).map_err(|error| format!("无法读取运行资源清单：{error}"))?;
    let package: Package =
        serde_json::from_slice(&bytes).map_err(|e| format!("运行资源清单无效：{e}"))?;
    if package.schema_version != 1 || package.backend != "Rust" || package.frontend != "Tauri" {
        return Err("这不是 Tauri + Rust 程序资源包，请使用匹配的安装包。".into());
    }
    let library = if cfg!(windows) {
        "pdfium.dll"
    } else if cfg!(target_os = "macos") {
        "libpdfium.dylib"
    } else {
        "libpdfium.so"
    };
    check_library(
        &paths.app_root.join("Resources/Pdf").join(library),
        b"FPDF_InitLibrary\0",
        "PDF",
    )?;
    require_file(
        &paths
            .app_root
            .join("Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf"),
        "中文报表字体",
    )?;
    if package.ocr {
        let directory = paths.app_root.join("sidecar/ocr");
        let worker = if cfg!(windows) {
            "exportdoc-ocr.exe"
        } else {
            "exportdoc-ocr"
        };
        let library = if cfg!(windows) {
            "onnxruntime.dll"
        } else if cfg!(target_os = "macos") {
            "libonnxruntime.dylib"
        } else {
            "libonnxruntime.so"
        };
        require_file(&directory.join(worker), "Rust OCR 工具")?;
        check_library(&directory.join(library), b"OrtGetApiBase\0", "OCR")?;
        for model in ["det/inference.onnx", "rec/inference.onnx"] {
            require_file(
                &paths.app_root.join("OcrModels/PaddleOCR/V6").join(model),
                "OCR 模型",
            )?;
        }
    }
    Ok(())
}

fn require_file(path: &Path, name: &str) -> Result<(), String> {
    ensure_safe_absolute(path)?;
    let metadata = fs::metadata(path)
        .map_err(|_| format!("程序包缺少或无法读取{name}，请完整解压或重新安装。"))?;
    if !metadata.is_file() || metadata.len() == 0 {
        return Err(format!("{name}资源无效，请重新安装。"));
    }
    Ok(())
}

fn check_library(path: &Path, symbol: &[u8], name: &str) -> Result<(), String> {
    require_file(path, name)?;
    // Only explicitly selected package libraries are loaded. Windows resolves
    // imports from their own directory and system libraries, never PATH/CWD.
    let result = unsafe {
        #[cfg(windows)]
        let library =
            libloading::os::windows::Library::load_with_flags(path, 0x00000100 | 0x00000800)
                .map(libloading::Library::from);
        #[cfg(not(windows))]
        let library = libloading::Library::new(path);
        library.and_then(|library| library.get::<*const std::ffi::c_void>(symbol).map(|_| ()))
    };
    result.map_err(|error| format!("{name} 运行库无法加载：{error}。请重新安装匹配当前架构的完整 Rust 程序包；无需安装 .NET。"))
}
