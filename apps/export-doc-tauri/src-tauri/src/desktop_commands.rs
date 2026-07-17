use std::{
    ffi::OsString,
    fs,
    io::{self, Write},
    path::{Path, PathBuf},
    process::{Command, Stdio},
};

use base64::{engine::general_purpose::STANDARD, Engine as _};
use tauri::Manager;

use crate::runtime_paths::RuntimePaths;

const MAX_OCR_PREVIEW_IMAGE_BYTES: u64 = 25 * 1024 * 1024;
const MAX_PDF_EXPORT_BYTES: usize = 25 * 1024 * 1024;
const MAX_FRONTEND_LOG_FIELD_LENGTH: usize = 8 * 1024;

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
pub(crate) fn select_report_template_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择报表模板文件",
        &[
            ("报表模板", &["html", "htm", "scriban", "txt"]),
            ("全部文件", &["*"]),
        ],
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

    fs::write(output_path, bytes)
        .map_err(|error| format!("无法保存 PDF '{}'：{error}", output_path.display()))
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
    if let Some(window) = app_handle.get_webview_window("main") {
        window.close().map_err(|error| error.to_string())?;
        return Ok(());
    }

    app_handle.exit(0);
    Ok(())
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
        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn write_pdf_file_rejects_non_pdf_content_and_extension() {
        let data_root = fresh_desktop_command_test_dir("reject-invalid-pdf");

        assert!(write_pdf_file(&data_root.join("plan.txt"), b"%PDF-1.4").is_err());
        assert!(write_pdf_file(&data_root.join("plan.pdf"), b"not a pdf").is_err());
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
