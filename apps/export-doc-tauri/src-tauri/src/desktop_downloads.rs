//! Browser downloads use the user's explicit destination, never a system default directory.
use tauri::webview::DownloadEvent;

pub(crate) fn handle(webview: tauri::Webview, event: DownloadEvent<'_>) -> bool {
    match event {
        DownloadEvent::Requested { url, destination } => {
            if !crate::window::allow_main_navigation(&url)
                && !(url.scheme() == "http"
                    && url.host_str() == Some("127.0.0.1")
                    && url.path().starts_with("/downloads/"))
            {
                return false;
            }
            let Some(name) = destination.file_name().and_then(|name| name.to_str()) else {
                return false;
            };
            let Some(path) = rfd::FileDialog::new()
                .set_parent(&webview.window())
                .set_title("选择文件保存位置")
                .set_file_name(name)
                .save_file()
            else {
                return false;
            };
            if !path.is_absolute() || path.file_name().is_none() {
                return false;
            }
            *destination = path;
        }
        DownloadEvent::Finished { success: false, .. } => {
            let _ = crate::write_tauri_error("Browser file download failed.");
            rfd::MessageDialog::new()
                .set_parent(&webview.window())
                .set_title("文件保存失败")
                .set_description("文件未能保存，请检查所选位置后重试。")
                .set_level(rfd::MessageLevel::Error)
                .show();
        }
        _ => {}
    }
    true
}
