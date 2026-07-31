use std::{
    ffi::OsString,
    fs::{self, OpenOptions},
    io::{self, Write},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::atomic::{AtomicBool, AtomicU64, Ordering},
};

use base64::{engine::general_purpose::STANDARD, Engine as _};
use tauri::Manager;

use crate::runtime_paths::{self, RuntimePaths};

const MAX_OCR_PREVIEW_IMAGE_BYTES: u64 = 25 * 1024 * 1024;
const MAX_PDF_EXPORT_BYTES: usize = 25 * 1024 * 1024;
const MAX_FRONTEND_LOG_FIELD_LENGTH: usize = 8 * 1024;
const PDF_TEMP_FILE_CREATE_ATTEMPTS: usize = 16;
static PDF_TEMP_FILE_COUNTER: AtomicU64 = AtomicU64::new(0);
static EXIT_CONFIRMED: AtomicBool = AtomicBool::new(false);

#[tauri::command]
pub(crate) fn select_single_window_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择单一窗口交换包",
        &[
            ("单一窗口交换包", &["swpkg", "edpkg"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_invoice_transfer_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择发票单据包",
        &[("发票单据包", &["edpkg"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_disaster_recovery_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择持卡机灾难恢复包",
        &[("持卡机灾难恢复包", &["edmrecovery"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_receipt_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择单一窗口回执文件",
        &[("回执文件", &["xml", "acd"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_receipt_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择单一窗口回执文件")
        .add_filter("回执文件", &["xml", "acd"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_pdf_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择 PDF 文件")
        .add_filter("PDF 文件", &["pdf"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_email_attachment_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择邮件附件")
        .add_filter(
            "常用附件",
            &["pdf", "xlsx", "xls", "docx", "doc", "zip", "txt"],
        )
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_customs_coo_attachment_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择原产地证附件")
        .add_filter("常用文件", &["pdf", "jpg", "jpeg", "png", "doc", "docx"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_letter_of_credit_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择信用证文件",
        &[
            (
                "信用证文件",
                &[
                    "pdf", "txt", "md", "csv", "json", "xml", "png", "jpg", "jpeg", "bmp", "gif",
                    "tif", "tiff", "webp",
                ],
            ),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_ocr_image_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择 OCR 图片",
        &[
            ("图片文件", &["png", "jpg", "jpeg", "bmp", "tif", "tiff"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_exporter_seal_image_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择出口商印章图片",
        &[
            ("图片文件", &["png", "jpg", "jpeg", "bmp"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn read_ocr_image_file_as_data_url(path: String) -> Result<String, String> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return Err("OCR 图片路径不能为空。".to_owned());
    }

    let input = PathBuf::from(trimmed);
    let metadata = fs::metadata(&input)
        .map_err(|error| format!("无法读取 OCR 图片 '{}': {error}", input.display()))?;
    if !metadata.is_file() {
        return Err("OCR 预览只能读取图片文件。".to_owned());
    }

    if metadata.len() > MAX_OCR_PREVIEW_IMAGE_BYTES {
        return Err("OCR 图片超过 25 MB 预览限制。".to_owned());
    }

    let Some(mime_type) = ocr_image_mime_type(&input) else {
        return Err("OCR 预览仅支持 PNG、JPG、BMP、TIFF 图片。".to_owned());
    };

    let bytes = fs::read(&input)
        .map_err(|error| format!("无法读取 OCR 图片 '{}': {error}", input.display()))?;
    Ok(format!(
        "data:{mime_type};base64,{}",
        STANDARD.encode(bytes)
    ))
}

#[tauri::command]
pub(crate) fn select_excel_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择 Excel 文件",
        &[
            ("Excel 文件", &["xlsx", "xlsm", "xltx", "xltm", "xls"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_directory(
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(apply_default_directory(
        rfd::FileDialog::new().set_title("选择目录"),
        default_directory,
    )
    .pick_folder()
    .map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_report_template_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择报表模板包",
        &[("报表模板包", &["edtpl", "zip"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_save_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择单一窗口交换包保存位置")
        .add_filter("单一窗口交换包", &["swpkg"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_invoice_transfer_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择发票单据包保存位置")
        .add_filter("发票单据包", &["edpkg"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_report_template_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择报表模板包保存位置")
        .add_filter("报表模板包", &["edtpl", "zip"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_pdf_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 PDF 保存位置")
        .add_filter("PDF 文件", &["pdf"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_zip_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 ZIP 保存位置")
        .add_filter("ZIP 文件", &["zip"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_excel_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 Excel 保存位置")
        .add_filter("Excel 文件", &["xlsx"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn save_pdf_file(path: String, base64_data: String) -> Result<(), String> {
    let output_path = PathBuf::from(path.trim());
    let bytes = STANDARD
        .decode(base64_data.trim())
        .map_err(|_| "PDF 数据无效，请重新生成。".to_owned())?;

    write_pdf_file(&output_path, &bytes)
}

fn write_pdf_file(output_path: &Path, bytes: &[u8]) -> Result<(), String> {
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

fn create_pdf_temp_file_with<F>(mut next_path: F) -> io::Result<(PathBuf, fs::File)>
where
    F: FnMut() -> PathBuf,
{
    let mut last_collision = None;
    for _ in 0..PDF_TEMP_FILE_CREATE_ATTEMPTS {
        let temp_path = next_path();
        match OpenOptions::new()
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

#[tauri::command]
pub(crate) fn open_path(path: String) -> Result<(), String> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return Err("路径不能为空。".to_owned());
    }

    let input = PathBuf::from(trimmed);
    let (target, is_file) = resolve_open_path_target(&input)?;

    open_existing_path(&target, is_file)
}

#[tauri::command]
pub(crate) fn get_runtime_storage_context(
    paths: tauri::State<'_, RuntimePaths>,
) -> runtime_paths::RuntimeStorageContext {
    runtime_paths::runtime_storage_context(&paths)
}

#[tauri::command]
pub(crate) fn schedule_data_root_migration(
    paths: tauri::State<'_, RuntimePaths>,
) -> Result<Option<runtime_paths::DataRootMigrationScheduleResult>, String> {
    if paths.portable {
        return Err(
            "便携版的数据目录固定为程序目录旁的 App_Data；如需迁移，请退出程序后复制完整便携目录。"
                .to_owned(),
        );
    }

    let mut dialog = rfd::FileDialog::new().set_title("选择新的业务数据目录（必须为空）");
    if let Some(parent) = paths.data_root.parent().filter(|path| path.is_dir()) {
        dialog = dialog.set_directory(parent);
    }
    let Some(target_root) = dialog.pick_folder() else {
        return Ok(None);
    };

    runtime_paths::schedule_data_root_migration(&paths, &target_root)
        .map(Some)
        .map_err(|error| error.to_string())
}

#[tauri::command]
pub(crate) fn log_frontend_error(
    paths: tauri::State<'_, RuntimePaths>,
    message: String,
    source: Option<String>,
    stack: Option<String>,
    url: Option<String>,
) -> Result<(), String> {
    fs::create_dir_all(&paths.log_root)
        .map_err(|error| format!("无法创建前端错误日志目录：{error}"))?;
    let log_path = paths.log_root.join("frontend-errors.log");
    fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_path)
        .and_then(|mut log| {
            writeln!(
                log,
                "\n=== Frontend error at {:?} ===\nurl: {}\nsource: {}\nmessage: {}\nstack:\n{}",
                std::time::SystemTime::now(),
                truncate_log_field(url.unwrap_or_default()),
                truncate_log_field(source.unwrap_or_default()),
                truncate_log_field(message),
                truncate_log_field(stack.unwrap_or_default())
            )
        })
        .map_err(|error| format!("无法写入前端错误日志 '{}': {error}", log_path.display()))
}

fn resolve_open_path_target(input: &Path) -> Result<(PathBuf, bool), String> {
    match fs::metadata(input) {
        Ok(metadata) => Ok((input.to_path_buf(), metadata.is_file())),
        Err(error) if error.kind() == io::ErrorKind::NotFound && input.extension().is_some() => {
            if let Some(parent) = input.parent() {
                if !parent.as_os_str().is_empty() {
                    if let Ok(parent_metadata) = fs::metadata(parent) {
                        if parent_metadata.is_dir() {
                            return Ok((parent.to_path_buf(), false));
                        }
                    }
                }
            }

            Err(format!("无法打开路径 '{}': {error}", input.display()))
        }
        Err(error) => Err(format!("无法打开路径 '{}': {error}", input.display())),
    }
}

#[tauri::command]
pub(crate) fn request_app_exit(app_handle: tauri::AppHandle) -> Result<(), String> {
    EXIT_CONFIRMED.store(true, Ordering::SeqCst);
    if let Some(window) = app_handle.get_webview_window("main") {
        if let Err(error) = window.close() {
            EXIT_CONFIRMED.store(false, Ordering::SeqCst);
            return Err(error.to_string());
        }
        return Ok(());
    }

    app_handle.exit(0);
    Ok(())
}

pub(crate) fn is_app_exit_confirmed() -> bool {
    EXIT_CONFIRMED.load(Ordering::SeqCst)
}

fn pick_file(title: &str, filters: &[(&str, &[&str])]) -> Option<String> {
    let mut dialog = rfd::FileDialog::new().set_title(title);
    for (name, extensions) in filters {
        dialog = dialog.add_filter(*name, *extensions);
    }

    dialog.pick_file().map(path_to_string)
}

fn apply_default_directory(
    dialog: rfd::FileDialog,
    default_directory: Option<String>,
) -> rfd::FileDialog {
    if let Some(path) = resolve_existing_directory_candidate(default_directory) {
        dialog.set_directory(path)
    } else {
        dialog
    }
}

fn resolve_existing_directory_candidate(default_directory: Option<String>) -> Option<PathBuf> {
    let directory = default_directory
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())?;
    let path = PathBuf::from(directory);
    fs::metadata(&path)
        .ok()
        .filter(|metadata| metadata.is_dir())
        .map(|_| path)
}

#[cfg(test)]
fn is_existing_directory_candidate(default_directory: Option<String>) -> bool {
    resolve_existing_directory_candidate(default_directory).is_some()
}

fn path_to_string(path: PathBuf) -> String {
    path.to_string_lossy().into_owned()
}

fn truncate_log_field(value: String) -> String {
    let mut output = String::new();
    for ch in value.chars().take(MAX_FRONTEND_LOG_FIELD_LENGTH) {
        output.push(ch);
    }

    if value.chars().count() > MAX_FRONTEND_LOG_FIELD_LENGTH {
        output.push_str("\n... truncated ...");
    }

    output
}

fn ocr_image_mime_type(path: &Path) -> Option<&'static str> {
    let extension = path.extension()?.to_string_lossy().to_ascii_lowercase();
    match extension.as_str() {
        "png" => Some("image/png"),
        "jpg" | "jpeg" => Some("image/jpeg"),
        "bmp" => Some("image/bmp"),
        "tif" | "tiff" => Some("image/tiff"),
        _ => None,
    }
}

#[cfg(windows)]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[cfg(target_os = "macos")]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[cfg(all(unix, not(target_os = "macos")))]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[derive(Debug, PartialEq, Eq)]
struct OpenCommandSpec {
    program: &'static str,
    program_name: &'static str,
    args: Vec<OsString>,
}

#[cfg(windows)]
fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let args = if reveal_file {
        vec![OsString::from(format!("/select,{}", path.display()))]
    } else {
        vec![path.as_os_str().to_owned()]
    };

    OpenCommandSpec {
        program: "explorer",
        program_name: "Windows Explorer",
        args,
    }
}

#[cfg(target_os = "macos")]
fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let mut args = Vec::new();
    if reveal_file {
        args.push(OsString::from("-R"));
    }

    args.push(path.as_os_str().to_owned());

    OpenCommandSpec {
        program: "open",
        program_name: "open",
        args,
    }
}

#[cfg(all(unix, not(target_os = "macos")))]
fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let target = if reveal_file {
        path.parent().unwrap_or(path)
    } else {
        path
    };

    OpenCommandSpec {
        program: "xdg-open",
        program_name: "xdg-open",
        args: vec![target.as_os_str().to_owned()],
    }
}

fn spawn_open_command(spec: OpenCommandSpec) -> Result<(), String> {
    let mut command = Command::new(spec.program);
    command.args(spec.args);
    spawn_detached(command, spec.program_name)
}

fn spawn_detached(mut command: Command, program_name: &str) -> Result<(), String> {
    command
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        command.creation_flags(CREATE_NO_WINDOW);
    }

    command
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("无法启动 {program_name}: {error}"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn open_path_rejects_blank_input_before_spawning() {
        let error = open_path("   ".to_owned()).unwrap_err();

        assert!(error.contains("路径不能为空"));
    }

    #[test]
    fn resolve_open_path_target_keeps_existing_file() {
        let data_root = fresh_desktop_command_test_dir("open-existing-file");
        let file_path = data_root.join("invoice.pdf");
        fs::write(&file_path, "pdf").unwrap();

        let (target, is_file) = resolve_open_path_target(&file_path).unwrap();

        assert_eq!(target, file_path);
        assert!(is_file);

        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn resolve_open_path_target_uses_parent_for_missing_output_file() {
        let data_root = fresh_desktop_command_test_dir("open-missing-output-file");
        let missing_file_path = data_root.join("invoice_template_2024AA001.pdf");

        let (target, is_file) = resolve_open_path_target(&missing_file_path).unwrap();

        assert_eq!(target, data_root);
        assert!(!is_file);

        let _ = fs::remove_dir_all(target);
    }

    #[test]
    fn resolve_open_path_target_rejects_missing_directory_without_extension() {
        let data_root = fresh_desktop_command_test_dir("open-missing-directory");
        let missing_directory_path = data_root.join("MissingDirectory");

        let error = resolve_open_path_target(&missing_directory_path).unwrap_err();

        assert!(error.contains("无法打开路径"));

        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn default_directory_candidates_require_existing_directory() {
        let data_root = fresh_desktop_command_test_dir("dialog-default-directory");
        let valid = data_root.join("Exports");
        fs::create_dir_all(&valid).unwrap();
        let file_path = data_root.join("not-a-directory.txt");
        fs::write(&file_path, "not a directory").unwrap();

        assert!(is_existing_directory_candidate(Some(
            valid.to_string_lossy().into_owned()
        )));
        assert!(!is_existing_directory_candidate(Some(
            file_path.to_string_lossy().into_owned()
        )));
        assert!(!is_existing_directory_candidate(Some(
            data_root.join("missing").to_string_lossy().into_owned()
        )));
        assert!(!is_existing_directory_candidate(Some("   ".to_owned())));
        assert!(!is_existing_directory_candidate(None));

        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn write_pdf_file_accepts_valid_pdf_in_selected_directory() {
        let data_root = fresh_desktop_command_test_dir("save-pdf");
        let output_path = data_root.join("container-loading-plan.pdf");

        write_pdf_file(&output_path, b"%PDF-1.4\n%%EOF").unwrap();

        assert_eq!(fs::read(&output_path).unwrap(), b"%PDF-1.4\n%%EOF");
        assert!(!fs::read_dir(&data_root).unwrap().any(|entry| {
            entry
                .unwrap()
                .file_name()
                .to_string_lossy()
                .ends_with(".tmp")
        }));
        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn write_pdf_file_atomically_replaces_existing_file_and_preserves_it_on_validation_failure() {
        let data_root = fresh_desktop_command_test_dir("replace-pdf");
        let output_path = data_root.join("report.pdf");
        fs::write(&output_path, b"%PDF-1.4\nold").unwrap();

        write_pdf_file(&output_path, b"%PDF-1.7\nnew").unwrap();
        assert_eq!(fs::read(&output_path).unwrap(), b"%PDF-1.7\nnew");

        assert!(write_pdf_file(&output_path, b"invalid").is_err());
        assert_eq!(fs::read(&output_path).unwrap(), b"%PDF-1.7\nnew");
        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn write_pdf_file_rejects_non_pdf_content_and_extension() {
        let data_root = fresh_desktop_command_test_dir("reject-invalid-pdf");

        assert!(write_pdf_file(&data_root.join("plan.txt"), b"%PDF-1.4").is_err());
        assert!(write_pdf_file(&data_root.join("plan.pdf"), b"not a pdf").is_err());
        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn pdf_temp_file_creation_retries_after_a_stale_name_collision() {
        let data_root = fresh_desktop_command_test_dir("pdf-temp-collision");
        let collision_path = data_root.join(".report.pdf.collision.tmp");
        let available_path = data_root.join(".report.pdf.available.tmp");
        fs::write(&collision_path, "stale").unwrap();
        let mut candidates = vec![collision_path.clone(), available_path.clone()].into_iter();

        let (selected_path, file) =
            create_pdf_temp_file_with(|| candidates.next().unwrap()).unwrap();
        drop(file);

        assert_eq!(selected_path, available_path);
        assert_eq!(fs::read_to_string(collision_path).unwrap(), "stale");
        let _ = fs::remove_dir_all(data_root);
    }

    #[cfg(windows)]
    #[test]
    fn windows_open_command_uses_explorer_for_directories() {
        let path = PathBuf::from(r"D:\ExportDocManager\App_Data");
        let spec = build_open_command(&path, false);

        assert_eq!(spec.program, "explorer");
        assert_eq!(spec.program_name, "Windows Explorer");
        assert_eq!(spec.args, vec![path.as_os_str().to_owned()]);
    }

    #[cfg(windows)]
    #[test]
    fn windows_open_command_reveals_files_with_select_argument() {
        let path = PathBuf::from(r"D:\ExportDocManager\App_Data\Database\data.db");
        let spec = build_open_command(&path, true);

        assert_eq!(spec.program, "explorer");
        assert_eq!(
            spec.args,
            vec![OsString::from(format!("/select,{}", path.display()))]
        );
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn macos_open_command_reveals_files_with_open_reveal_argument() {
        let path = PathBuf::from("/Applications/ExportDocManager/App_Data/Database/data.db");
        let spec = build_open_command(&path, true);

        assert_eq!(spec.program, "open");
        assert_eq!(spec.args, vec![OsString::from("-R"), path.into_os_string()]);
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    #[test]
    fn linux_open_command_reveals_files_by_opening_parent_directory() {
        let path = PathBuf::from("/opt/exportdoc/App_Data/Database/data.db");
        let spec = build_open_command(&path, true);

        assert_eq!(spec.program, "xdg-open");
        assert_eq!(
            spec.args,
            vec![OsString::from("/opt/exportdoc/App_Data/Database")]
        );
    }

    fn fresh_desktop_command_test_dir(name: &str) -> PathBuf {
        let root = std::env::current_dir()
            .unwrap()
            .join("target")
            .join("desktop-command-tests")
            .join(format!("{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).unwrap();
        root
    }
}
