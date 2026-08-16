use std::{
    fs,
    io::Write,
    sync::{
        atomic::{AtomicBool, Ordering},
        LazyLock,
    },
};

use regex::{Captures, Regex};
use tauri::Manager;
use url::Url;

use crate::runtime_paths::{self, RuntimePaths};

const MAX_FRONTEND_LOG_FIELD_LENGTH: usize = 8 * 1024;
static EXIT_CONFIRMED: AtomicBool = AtomicBool::new(false);
static HTTP_URL_PATTERN: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r#"(?i)https?://[^\s"'<>]+"#).expect("valid HTTP URL regex"));
static BEARER_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}").expect("valid bearer regex")
});
static SENSITIVE_ASSIGNMENT_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(
        r#"(?i)(?P<prefix>["']?(?:password|passwd|pwd|secret|api[-_]?key|token|credential|access[-_]?key|signing[-_]?key|encryption[-_]?key|connection[-_]?string)["']?\s*(?:=|:)\s*["']?)[^"'\s,;}\]]+"#,
    )
    .expect("valid sensitive assignment regex")
});
static EMAIL_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b").expect("valid email regex")
});
static WINDOWS_USER_PATH_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)\b(?P<prefix>[A-Z]:\\Users)\\[^\\\s"'<>|]+"#)
        .expect("valid Windows user path regex")
});
static UNIX_HOME_PATH_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?P<prefix>(?:^|[^A-Za-z0-9_])/(?:Users|home))/[^/\s"'<>]+"#)
        .expect("valid Unix home path regex")
});

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
                sanitize_log_url(url.unwrap_or_default()),
                sanitize_log_field(source.unwrap_or_default()),
                sanitize_log_field(message),
                sanitize_log_field(stack.unwrap_or_default())
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

fn sanitize_log_field(value: String) -> String {
    let sanitized = HTTP_URL_PATTERN
        .replace_all(&value, |captures: &Captures<'_>| {
            sanitize_http_url(&captures[0])
        })
        .into_owned();
    let sanitized = BEARER_PATTERN
        .replace_all(&sanitized, "Bearer [REDACTED]")
        .into_owned();
    let sanitized = SENSITIVE_ASSIGNMENT_PATTERN
        .replace_all(&sanitized, |captures: &Captures<'_>| {
            format!("{}[REDACTED]", &captures["prefix"])
        })
        .into_owned();
    let sanitized = EMAIL_PATTERN
        .replace_all(&sanitized, "[REDACTED_EMAIL]")
        .into_owned();
    let sanitized = WINDOWS_USER_PATH_PATTERN
        .replace_all(&sanitized, |captures: &Captures<'_>| {
            format!("{}\\[REDACTED]", &captures["prefix"])
        })
        .into_owned();
    let sanitized = UNIX_HOME_PATH_PATTERN
        .replace_all(&sanitized, |captures: &Captures<'_>| {
            format!("{}/[REDACTED]", &captures["prefix"])
        })
        .into_owned();

    let mut output = String::new();
    for ch in sanitized.chars().take(MAX_FRONTEND_LOG_FIELD_LENGTH) {
        output.push(if ch.is_control() && !matches!(ch, '\r' | '\n' | '\t') {
            ' '
        } else {
            ch
        });
    }

    if sanitized.chars().count() > MAX_FRONTEND_LOG_FIELD_LENGTH {
        output.push_str("\n... truncated ...");
    }

    output
}

fn sanitize_log_url(value: String) -> String {
    let Ok(url) = Url::parse(&value) else {
        return "[REDACTED_URL]".to_owned();
    };
    if !matches!(url.scheme(), "http" | "https") {
        return "[REDACTED_URL]".to_owned();
    }
    sanitize_http_url(&value)
}

fn sanitize_http_url(value: &str) -> String {
    let Ok(mut url) = Url::parse(value) else {
        return "[REDACTED_URL]".to_owned();
    };
    if !matches!(url.scheme(), "http" | "https") {
        return "[REDACTED_URL]".to_owned();
    }

    let _ = url.set_username("");
    let _ = url.set_password(None);
    url.set_query(None);
    url.set_fragment(None);
    let path = url
        .path_segments()
        .map(|segments| {
            segments
                .map(|segment| {
                    if segment.len() >= 24
                        && segment
                            .chars()
                            .all(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '-' | '_' | '.'))
                    {
                        "[REDACTED]"
                    } else {
                        segment
                    }
                })
                .collect::<Vec<_>>()
                .join("/")
        })
        .unwrap_or_default();
    url.set_path(&format!("/{path}"));
    url.to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::desktop_file_dialog_commands::{
        is_existing_directory_candidate, read_exporter_seal_image_file_as_data_url,
        MAX_EXPORTER_SEAL_IMAGE_BYTES,
    };
    use crate::desktop_open_commands::{build_open_command, open_path, resolve_open_path_target};
    use base64::{engine::general_purpose::STANDARD, Engine as _};
    use std::ffi::OsString;
    use std::path::PathBuf;

    #[test]
    fn frontend_log_fields_remove_credentials_and_personal_paths() {
        let sanitized = sanitize_log_field(
            r#"Authorization: Bearer abcdefghijklmnopqrstuvwxyz password=plain-secret operator@example.com C:\Users\bridge\workspace\app.ts request https://example.test/jobs/0123456789abcdef0123456789abcdef?token=query-secret"#
                .to_owned(),
        );

        for secret in [
            "abcdefghijklmnopqrstuvwxyz",
            "plain-secret",
            "operator@example.com",
            "bridge",
            "query-secret",
        ] {
            assert!(!sanitized.contains(secret), "sanitized log leaked {secret}");
        }
        assert!(sanitized.contains("Bearer [REDACTED]"));
        assert!(sanitized.contains("[REDACTED_EMAIL]"));
        assert!(sanitized.contains(r"C:\Users\[REDACTED]"));
        assert!(sanitized.contains("https://example.test/jobs/[REDACTED]"));
    }

    #[test]
    fn frontend_log_urls_keep_only_safe_http_location() {
        assert_eq!(
            sanitize_log_url(
                "https://user:pass@example.test/invoices/0123456789abcdef0123456789abcdef?token=query-secret#details"
                    .to_owned(),
            ),
            "https://example.test/invoices/[REDACTED]"
        );
        assert_eq!(
            sanitize_log_url("file:///Users/bridge/private.log".to_owned()),
            "[REDACTED_URL]"
        );
    }

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
