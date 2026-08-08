use std::{
    fs,
    io::Write,
    sync::atomic::{AtomicBool, Ordering},
};

use tauri::Manager;

use crate::runtime_paths::{self, RuntimePaths};

const MAX_FRONTEND_LOG_FIELD_LENGTH: usize = 8 * 1024;
static EXIT_CONFIRMED: AtomicBool = AtomicBool::new(false);

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
    crate::log_rotation::open_append_log_file(&log_path)
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

#[tauri::command]
pub(crate) fn request_app_exit(app_handle: tauri::AppHandle) -> Result<(), String> {
    confirm_app_exit();
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

pub(crate) fn confirm_app_exit() {
    EXIT_CONFIRMED.store(true, Ordering::SeqCst);
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::desktop_file_dialog_commands::{
        is_existing_directory_candidate, read_exporter_seal_image_file_as_data_url,
        MAX_EXPORTER_SEAL_IMAGE_BYTES,
    };
    use crate::desktop_open_commands::{build_open_command, open_path, resolve_open_path_target};
    use crate::desktop_pdf_commands::{
        create_pdf_temp_file_with, is_pdf_base64_length_allowed, write_pdf_file,
        MAX_PDF_EXPORT_BASE64_BYTES,
    };
    use base64::{engine::general_purpose::STANDARD, Engine as _};
    use std::ffi::OsString;
    use std::path::PathBuf;

    #[test]
    fn exporter_seal_reader_accepts_managed_upload_formats() {
        let data_root = fresh_desktop_command_test_dir("read-exporter-seal");
        let png_path = data_root.join("boen-baoguan.png");
        let png_bytes = STANDARD
            .decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgQIAQbT2ZQAAAABJRU5ErkJggg==")
            .unwrap();
        fs::write(&png_path, png_bytes).unwrap();

        let data_url =
            read_exporter_seal_image_file_as_data_url(png_path.to_string_lossy().into_owned())
                .unwrap();

        assert!(data_url.starts_with("data:image/png;base64,"));
        let _ = fs::remove_dir_all(data_root);
    }

    #[test]
    fn exporter_seal_reader_rejects_blank_oversized_and_mismatched_files() {
        assert!(read_exporter_seal_image_file_as_data_url("   ".to_owned())
            .unwrap_err()
            .contains("路径不能为空"));

        let data_root = fresh_desktop_command_test_dir("reject-exporter-seal");
        let oversized_path = data_root.join("oversized.png");
        fs::write(
            &oversized_path,
            vec![0_u8; MAX_EXPORTER_SEAL_IMAGE_BYTES as usize + 1],
        )
        .unwrap();
        assert!(read_exporter_seal_image_file_as_data_url(
            oversized_path.to_string_lossy().into_owned(),
        )
        .unwrap_err()
        .contains("不能超过 5 MB"));

        let mismatched_path = data_root.join("renamed.jpg");
        fs::write(
            &mismatched_path,
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a],
        )
        .unwrap();
        assert!(read_exporter_seal_image_file_as_data_url(
            mismatched_path.to_string_lossy().into_owned(),
        )
        .unwrap_err()
        .contains("扩展名与实际图片格式不一致"));

        let _ = fs::remove_dir_all(data_root);
    }

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
    fn pdf_base64_length_is_rejected_before_an_oversized_decode() {
        assert!(is_pdf_base64_length_allowed(MAX_PDF_EXPORT_BASE64_BYTES));
        assert!(!is_pdf_base64_length_allowed(0));
        assert!(!is_pdf_base64_length_allowed(
            MAX_PDF_EXPORT_BASE64_BYTES + 1
        ));
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
