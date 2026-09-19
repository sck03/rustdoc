use serde::Deserialize;
use std::{
    fs,
    io::Write,
    path::{Component, Path, PathBuf},
};
use unicode_normalization::UnicodeNormalization;

#[derive(Clone)]
pub struct RuntimePaths {
    pub app_root: PathBuf,
    pub data_root: PathBuf,
    pub log_root: PathBuf,
    pub cache_root: PathBuf,
    pub font_path: PathBuf,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct PackageMarker {
    schema_version: u32,
    purpose: String,
}

impl RuntimePaths {
    pub fn server(app_root: &Path, data_root: &Path) -> Result<Self, String> {
        ensure_safe_absolute(app_root)?;
        ensure_safe_absolute(data_root)?;
        if !app_root.is_dir() {
            return Err("AppRoot 必须是现有程序目录。".into());
        }
        let log_root = data_root.join("Logs");
        let cache_root = data_root.join("Cache").join("Native");
        for path in [data_root, &log_root, &cache_root] {
            ensure_safe_absolute(path)?;
            fs::create_dir_all(path).map_err(|error| error.to_string())?;
            ensure_safe_absolute(path)?;
        }
        let probe = cache_root.join(format!("write-probe-{}", nonce()?));
        drop(
            fs::OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&probe)
                .map_err(|error| format!("数据目录不可写：{error}"))?,
        );
        fs::remove_file(probe).map_err(|error| error.to_string())?;
        let font_path = app_root
            .join("Resources")
            .join("Fonts")
            .join("OpenSource")
            .join("NotoSansCJKsc-Regular.otf");
        ensure_safe_absolute(&font_path)?;
        Ok(Self {
            app_root: app_root.to_path_buf(),
            data_root: data_root.to_path_buf(),
            log_root,
            cache_root,
            font_path,
        })
    }
    pub fn open(root: &Path, validation_run: bool) -> Result<Self, String> {
        ensure_safe_absolute(root)?;
        let app_root =
            fs::canonicalize(root).map_err(|error| format!("程序目录不可读：{error}"))?;
        let marker = app_root.join("exportdoc-native-package.json");
        ensure_safe_absolute(&marker)?;
        let marker: PackageMarker = serde_json::from_slice(
            &fs::read(&marker)
                .map_err(|_| "请选择独立的 Rust 原生程序目录。请先运行原生程序构建脚本。")?,
        )
        .map_err(|error| format!("原生程序包标记无效：{error}"))?;
        if marker.schema_version != 1 || marker.purpose != "rust-native-application" {
            return Err("这不是受支持的独立原生程序包。".into());
        }
        let data_name = if validation_run {
            format!("ValidationData-{}", nonce()?)
        } else {
            "App_Data".into()
        };
        let data_root = app_root.join(data_name);
        let log_root = data_root.join("Logs");
        let cache_root = data_root.join("Cache").join("Native");
        for directory in [&data_root, &log_root, &cache_root] {
            ensure_safe_absolute(directory)?;
            fs::create_dir_all(directory).map_err(|error| error.to_string())?;
            ensure_safe_absolute(directory)?;
        }
        let probe = cache_root.join(format!("write-probe-{}", nonce()?));
        let file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&probe)
            .map_err(|error| format!("验证数据目录不可写：{error}"))?;
        drop(file);
        fs::remove_file(probe).map_err(|error| error.to_string())?;
        let font_path = app_root
            .join("Resources")
            .join("Fonts")
            .join("OpenSource")
            .join("NotoSansCJKsc-Regular.otf");
        ensure_safe_absolute(&font_path)?;
        if !font_path.is_file() {
            return Err("验证包缺少已审核的 Noto CJK 字体。请重新构建验证包。".into());
        }
        Ok(Self {
            app_root,
            data_root,
            log_root,
            cache_root,
            font_path,
        })
    }
    pub fn pdfium_path(&self) -> PathBuf {
        self.app_root
            .join("Resources")
            .join("Pdf")
            .join(if cfg!(windows) {
                "pdfium.dll"
            } else if cfg!(target_os = "macos") {
                "libpdfium.dylib"
            } else {
                "libpdfium.so"
            })
    }
}

pub fn nonce() -> Result<String, String> {
    let mut bytes = [0u8; 16];
    getrandom::fill(&mut bytes).map_err(|error| format!("无法生成安全随机数：{error}"))?;
    Ok(bytes.iter().map(|byte| format!("{byte:02x}")).collect())
}

pub fn ensure_safe_absolute(path: &Path) -> Result<(), String> {
    if !path.is_absolute()
        || path
            .components()
            .any(|part| matches!(part, Component::ParentDir | Component::CurDir))
    {
        return Err("路径必须是没有跳转段的绝对路径。".into());
    }
    if path.parent().is_none() {
        return Err("不能使用磁盘根目录。".into());
    }
    let mut current = PathBuf::new();
    for component in path.components() {
        current.push(component);
        if matches!(component, Component::Prefix(_)) {
            continue;
        }
        match fs::symlink_metadata(&current) {
            Ok(metadata) => {
                let mut link = metadata.file_type().is_symlink();
                #[cfg(windows)]
                {
                    use std::os::windows::fs::MetadataExt;
                    link |= metadata.file_attributes() & 0x400 != 0;
                }
                if link {
                    return Err(format!("不允许符号链接或联接点：{}", current.display()));
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(format!("无法检查路径 {}：{error}", current.display())),
        }
    }
    Ok(())
}

pub fn valid_file_name(name: &str) -> bool {
    if name.is_empty()
        || name.chars().count() > 180
        || name.ends_with(['.', ' '])
        || name.nfc().collect::<String>() != name
        || name
            .chars()
            .any(|ch| ch.is_control() || "<>:\"/\\|?*".contains(ch))
    {
        return false;
    }
    let stem = name.split('.').next().unwrap_or("").to_ascii_uppercase();
    !matches!(
        stem.as_str(),
        "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$"
    ) && !(stem.starts_with("COM") || stem.starts_with("LPT"))
        .then(|| &stem[3..])
        .is_some_and(|end| {
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "¹", "²", "³"].contains(&end)
        })
}

pub fn suggested_pdf_name(invoice_no: &str) -> String {
    let name: String = invoice_no
        .nfc()
        .map(|ch| {
            if ch.is_control() || "<>:\"/\\|?*".contains(ch) {
                '_'
            } else {
                ch
            }
        })
        .take(120)
        .collect();
    let name = format!("{}.pdf", name.trim_end_matches(['.', ' ']));
    if valid_file_name(&name) {
        name
    } else {
        "Invoice.pdf".into()
    }
}

/// The caller must obtain this destination from an explicit file dialog or CLI
/// argument. Never falls back to a default export directory.
pub fn save_pdf(path: &Path, bytes: &[u8]) -> Result<(), String> {
    ensure_safe_absolute(path)?;
    let name = path
        .file_name()
        .and_then(|value| value.to_str())
        .ok_or("文件名不是有效 Unicode。")?;
    if !valid_file_name(name)
        || !path
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("pdf"))
    {
        return Err("请选择合法的 .pdf 文件名。".into());
    }
    if !bytes.starts_with(b"%PDF-") {
        return Err("后端结果不是有效的 PDF 文件。".into());
    }
    atomic_write(path, bytes)
}

pub fn atomic_write(path: &Path, bytes: &[u8]) -> Result<(), String> {
    atomic_write_with(path, |output| {
        output.write_all(bytes).map_err(|error| error.to_string())
    })
}

/// Publish a completely written file; cancellation or IO failure leaves the destination intact.
pub fn atomic_write_with(
    path: &Path,
    write: impl FnOnce(&mut fs::File) -> Result<(), String>,
) -> Result<(), String> {
    ensure_safe_absolute(path)?;
    let parent = path.parent().ok_or("目标文件没有父目录。")?;
    if !parent.is_dir() {
        return Err("目标目录不存在。".into());
    }
    let temporary = parent.join(format!(".native-write-{}.tmp", nonce()?));
    let result = (|| {
        let mut output = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temporary)
            .map_err(|error| error.to_string())?;
        write(&mut output)?;
        output.sync_all().map_err(|error| error.to_string())?;
        drop(output);
        ensure_safe_absolute(path)?;
        fs::rename(&temporary, path).map_err(|error| format!("写入目标失败，原文件已保留：{error}"))
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn failed_streaming_export_preserves_the_destination_and_cleans_temporary_files() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .join(".codex-runtime")
            .join("atomic-export-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(&root).unwrap();
        let target = root.join("selected-file.bin");
        fs::write(&target, b"original").unwrap();
        let result = atomic_write_with(&target, |output| {
            output.write_all(b"partial output").unwrap();
            Err("cancelled or failed source".into())
        });
        assert!(result.is_err());
        assert_eq!(fs::read(&target).unwrap(), b"original");
        assert_eq!(fs::read_dir(&root).unwrap().count(), 1);
        atomic_write_with(&target, |output| {
            output
                .write_all(b"complete output")
                .map_err(|error| error.to_string())
        })
        .unwrap();
        assert_eq!(fs::read(&target).unwrap(), b"complete output");
        fs::remove_file(target).unwrap();
        fs::remove_dir(root).unwrap();
    }
    #[test]
    fn export_names_are_portable_and_normalized() {
        for name in [
            "CON.pdf",
            "NUL",
            "COM1.pdf",
            "LPT¹.pdf",
            "a:b.pdf",
            "end.pdf ",
            "bad?.pdf",
            "e\u{301}.pdf",
        ] {
            assert!(!valid_file_name(name), "{name}");
        }
        assert!(valid_file_name("发票-é.pdf"));
        assert_eq!(suggested_pdf_name("客户/2026:001"), "客户_2026_001.pdf");
    }
    #[test]
    fn relative_and_parent_paths_are_rejected() {
        assert!(ensure_safe_absolute(Path::new("../out.pdf")).is_err());
    }
}
