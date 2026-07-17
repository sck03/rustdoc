#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    sync::OnceLock,
};

use tauri::Manager;

mod desktop_commands;
mod runtime_layout;
mod runtime_paths;
mod sidecar;
mod tauri_updater_commands;
mod window;

static RUNTIME_DIAGNOSTIC_LOG_ROOT: OnceLock<PathBuf> = OnceLock::new();

fn main() {
    install_panic_log_hook();
    if let Err(error) = run_tauri_app() {
        let _ = write_tauri_error(&format!("Tauri startup failed: {error}"));
        let bootstrap_log_path = write_bootstrap_error(&error);
        let diagnostic_hint = bootstrap_log_path
            .map(|path| format!("诊断日志已写入：{}", path.display()))
            .unwrap_or_else(|| {
                "诊断日志无法写入运行数据目录或程序目录。请复制本弹窗中的错误信息，并检查数据目录和安装目录权限。"
                    .to_owned()
            });
        rfd::MessageDialog::new()
            .set_level(rfd::MessageLevel::Error)
            .set_title("出口单证管理系统启动失败")
            .set_description(format!("程序启动失败：{error}\n\n{diagnostic_hint}"))
            .set_buttons(rfd::MessageButtons::Ok)
            .show();
        std::process::exit(1);
    }
}

fn run_tauri_app() -> tauri::Result<()> {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            desktop_commands::select_single_window_package_file,
            desktop_commands::select_invoice_transfer_package_file,
            desktop_commands::select_receipt_file,
            desktop_commands::select_receipt_files,
            desktop_commands::select_pdf_files,
            desktop_commands::select_email_attachment_files,
            desktop_commands::select_customs_coo_attachment_files,
            desktop_commands::select_letter_of_credit_file,
            desktop_commands::select_ocr_image_file,
            desktop_commands::select_exporter_seal_image_file,
            desktop_commands::read_ocr_image_file_as_data_url,
            desktop_commands::select_excel_file,
            desktop_commands::select_directory,
            desktop_commands::select_report_template_package_file,
            desktop_commands::select_report_template_file,
            desktop_commands::select_save_package_path,
            desktop_commands::select_save_invoice_transfer_package_path,
            desktop_commands::select_save_report_template_package_path,
            desktop_commands::select_save_pdf_path,
            desktop_commands::select_save_zip_path,
            desktop_commands::select_save_excel_path,
            desktop_commands::save_pdf_file,
            desktop_commands::open_path,
            desktop_commands::log_frontend_error,
            desktop_commands::request_app_exit,
            tauri_updater_commands::check_tauri_update,
            tauri_updater_commands::install_tauri_update,
            sidecar::get_desktop_runtime_context
        ])
        .plugin(tauri_plugin_updater::Builder::new().build())
        .setup(|app| {
            let paths = runtime_paths::prepare_runtime_paths(app)?;
            set_runtime_diagnostic_log_root(&paths.log_root);
            runtime_layout::validate_runtime_layout(&paths)?;
            let sidecar = sidecar::start_sidecar(&paths)?;
            let api_base_url = sidecar.api_base_url.clone();
            let desktop_access_token = sidecar.desktop_access_token.clone();
            app.manage(sidecar::SidecarState::new(
                sidecar.child,
                api_base_url.clone(),
                desktop_access_token,
            ));
            app.manage(paths.clone());
            window::open_main_window(app, &paths)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if matches!(event, tauri::WindowEvent::CloseRequested { .. })
                && window.label() == "main"
            {
                sidecar::run_shutdown_maintenance(window.app_handle());
                sidecar::stop_sidecar(window.app_handle());
            }
        })
        .run(tauri::generate_context!())
}

fn set_runtime_diagnostic_log_root(log_root: &Path) {
    let _ = RUNTIME_DIAGNOSTIC_LOG_ROOT.set(log_root.to_path_buf());
}

fn write_bootstrap_error(error: &tauri::Error) -> Option<PathBuf> {
    append_diagnostic_log(
        "tauri-bootstrap-error.log",
        "ExportDocManager Tauri bootstrap failed",
        &error.to_string(),
    )
}

fn install_panic_log_hook() {
    std::panic::set_hook(Box::new(|panic_info| {
        let _ = write_tauri_error(&format!("Tauri panic: {panic_info}"));
    }));
}

fn write_tauri_error(message: &str) -> Option<PathBuf> {
    append_diagnostic_log("tauri-errors.log", "ExportDocManager Tauri error", message)
}

fn append_diagnostic_log(file_name: &str, title: &str, message: &str) -> Option<PathBuf> {
    let roots = diagnostic_log_root_candidates();
    append_diagnostic_log_to_roots(&roots, file_name, title, message)
}

fn diagnostic_log_root_candidates() -> Vec<PathBuf> {
    let mut roots = Vec::new();

    if let Some(log_root) = RUNTIME_DIAGNOSTIC_LOG_ROOT.get() {
        push_unique_path(&mut roots, log_root.clone());
    } else if let Some(data_root) = runtime_paths::explicit_data_root_hint() {
        push_unique_path(&mut roots, data_root.join("Logs"));
    }

    if let Ok(exe_dir) = runtime_paths::current_exe_dir() {
        push_unique_path(&mut roots, exe_dir.join("logs"));
    }

    roots
}

fn push_unique_path(paths: &mut Vec<PathBuf>, path: PathBuf) {
    if !paths.iter().any(|existing| existing == &path) {
        paths.push(path);
    }
}

fn append_diagnostic_log_to_roots(
    roots: &[PathBuf],
    file_name: &str,
    title: &str,
    message: &str,
) -> Option<PathBuf> {
    for log_root in roots {
        if fs::create_dir_all(log_root).is_err() {
            continue;
        }

        let log_path = log_root.join(file_name);
        let write_result = fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_path)
            .and_then(|mut log| {
                writeln!(
                    log,
                    "\n=== {title} at {:?} ===\n{message}",
                    std::time::SystemTime::now()
                )
            });
        if write_result.is_ok() {
            return Some(log_path);
        }
    }

    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn diagnostic_log_writer_falls_back_to_next_writable_root() {
        let test_root = fresh_test_dir("diagnostic-log-fallback");
        fs::create_dir_all(&test_root).unwrap();
        let unusable_root = test_root.join("unusable-root");
        fs::write(&unusable_root, "not a directory").unwrap();
        let writable_root = test_root.join("runtime-data").join("Logs");

        let written_path = append_diagnostic_log_to_roots(
            &[unusable_root, writable_root.clone()],
            "tauri-bootstrap-error.log",
            "bootstrap failed",
            "sidecar was not found",
        )
        .expect("the second diagnostic root should be writable");

        assert_eq!(
            written_path,
            writable_root.join("tauri-bootstrap-error.log")
        );
        let content = fs::read_to_string(&written_path).unwrap();
        assert!(content.contains("bootstrap failed"));
        assert!(content.contains("sidecar was not found"));

        let _ = fs::remove_dir_all(test_root);
    }

    #[test]
    fn diagnostic_log_writer_reports_failure_when_no_root_is_writable() {
        let test_root = fresh_test_dir("diagnostic-log-unwritable");
        fs::create_dir_all(&test_root).unwrap();
        let first = test_root.join("first");
        let second = test_root.join("second");
        fs::write(&first, "not a directory").unwrap();
        fs::write(&second, "not a directory").unwrap();

        assert!(append_diagnostic_log_to_roots(
            &[first, second],
            "tauri-errors.log",
            "startup failed",
            "permission denied",
        )
        .is_none());

        let _ = fs::remove_dir_all(test_root);
    }

    fn fresh_test_dir(name: &str) -> PathBuf {
        let target_root = std::env::var_os("CARGO_TARGET_DIR")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("target"));
        let root = target_root
            .join("diagnostic-log-tests")
            .join(format!("{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        root
    }
}
