use std::{
    fs,
    io::{self, Write},
    path::{Path, PathBuf},
    sync::atomic::{AtomicU64, Ordering},
};

use base64::{engine::general_purpose::STANDARD, Engine as _};

pub(crate) const MAX_PDF_EXPORT_BYTES: usize = 25 * 1024 * 1024;
pub(crate) const MAX_PDF_EXPORT_BASE64_BYTES: usize = MAX_PDF_EXPORT_BYTES.div_ceil(3) * 4;
const PDF_TEMP_FILE_CREATE_ATTEMPTS: usize = 16;
static PDF_TEMP_FILE_COUNTER: AtomicU64 = AtomicU64::new(0);

#[tauri::command]
pub(crate) fn save_pdf_file(path: String, base64_data: String) -> Result<(), String> {
    let output_path = PathBuf::from(path.trim());
    let encoded = base64_data.trim();
    if !is_pdf_base64_length_allowed(encoded.len()) {
        return Err("PDF 数据无效或超过 25 MB。".to_owned());
    }
    let bytes = STANDARD
        .decode(encoded)
        .map_err(|_| "PDF 数据无效，请重新生成。".to_owned())?;

    write_pdf_file(&output_path, &bytes)
}

pub(crate) fn is_pdf_base64_length_allowed(encoded_length: usize) -> bool {
    encoded_length > 0 && encoded_length <= MAX_PDF_EXPORT_BASE64_BYTES
}

pub(crate) fn write_pdf_file(output_path: &Path, bytes: &[u8]) -> Result<(), String> {
    if output_path.as_os_str().is_empty() {
        return Err("PDF 保存路径不能为空。".to_owned());
    }
    if !output_path
        .extension()
        .and_then(|value| value.to_str())
        .is_some_and(|value| value.eq_ignore_ascii_case("pdf"))
    {
        return Err("PDF 保存路径必须以 .pdf 结尾。".to_owned());
    }
    if bytes.is_empty() || bytes.len() > MAX_PDF_EXPORT_BYTES || !bytes.starts_with(b"%PDF-") {
        return Err("PDF 文件内容无效或超过 25 MB。".to_owned());
    }
    let parent = output_path
        .parent()
        .filter(|value| !value.as_os_str().is_empty())
        .ok_or_else(|| "PDF 保存目录无效。".to_owned())?;
    if !parent.is_dir() {
        return Err("PDF 保存目录不存在，请重新选择保存位置。".to_owned());
    }

    let (temp_path, mut temp_file) = create_pdf_temp_file(output_path).map_err(|error| {
        format!(
            "无法创建 PDF 临时文件（目录 '{}'）：{error}",
            parent.display()
        )
    })?;
    let result = (|| -> Result<(), String> {
        temp_file
            .write_all(bytes)
            .and_then(|_| temp_file.flush())
            .and_then(|_| temp_file.sync_all())
            .map_err(|error| {
                format!(
                    "无法完整写入 PDF 临时文件 '{}'：{error}",
                    temp_path.display()
                )
            })?;
        drop(temp_file);
        replace_file_atomically(&temp_path, output_path)
            .map_err(|error| format!("无法保存 PDF '{}'：{error}", output_path.display()))
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp_path);
    }
    result
}

fn create_pdf_temp_file(output_path: &Path) -> io::Result<(PathBuf, fs::File)> {
    create_pdf_temp_file_with(|| build_pdf_temp_path(output_path))
}

pub(crate) fn create_pdf_temp_file_with<F>(mut next_path: F) -> io::Result<(PathBuf, fs::File)>
where
    F: FnMut() -> PathBuf,
{
    let mut last_collision = None;
    for _ in 0..PDF_TEMP_FILE_CREATE_ATTEMPTS {
        let temp_path = next_path();
        match fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temp_path)
        {
            Ok(file) => return Ok((temp_path, file)),
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {
                last_collision = Some(error);
            }
            Err(error) => return Err(error),
        }
    }

    Err(last_collision.unwrap_or_else(|| {
        io::Error::new(
            io::ErrorKind::AlreadyExists,
            "could not allocate a unique PDF temporary file",
        )
    }))
}

fn build_pdf_temp_path(output_path: &Path) -> PathBuf {
    let parent = output_path.parent().unwrap_or_else(|| Path::new("."));
    let file_name = output_path
        .file_name()
        .and_then(|value| value.to_str())
        .unwrap_or("export.pdf");
    let sequence = PDF_TEMP_FILE_COUNTER.fetch_add(1, Ordering::Relaxed);
    parent.join(format!(
        ".{file_name}.{}.{}.tmp",
        std::process::id(),
        sequence
    ))
}

#[cfg(windows)]
fn replace_file_atomically(source: &Path, target: &Path) -> io::Result<()> {
    use std::os::windows::ffi::OsStrExt;

    const MOVEFILE_REPLACE_EXISTING: u32 = 0x1;
    const MOVEFILE_WRITE_THROUGH: u32 = 0x8;

    #[link(name = "Kernel32")]
    extern "system" {
        fn MoveFileExW(
            existing_file_name: *const u16,
            new_file_name: *const u16,
            flags: u32,
        ) -> i32;
    }

    let source_wide: Vec<u16> = source.as_os_str().encode_wide().chain(Some(0)).collect();
    let target_wide: Vec<u16> = target.as_os_str().encode_wide().chain(Some(0)).collect();
    let result = unsafe {
        MoveFileExW(
            source_wide.as_ptr(),
            target_wide.as_ptr(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH,
        )
    };
    if result == 0 {
        Err(io::Error::last_os_error())
    } else {
        Ok(())
    }
}

#[cfg(not(windows))]
fn replace_file_atomically(source: &Path, target: &Path) -> io::Result<()> {
    fs::rename(source, target)?;
    if let Some(parent) = target.parent() {
        if let Ok(directory) = fs::File::open(parent) {
            let _ = directory.sync_all();
        }
    }
    Ok(())
}
