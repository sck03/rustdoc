use std::error::Error;
use std::path::{Path, PathBuf};

use tauri::{WebviewUrl, WebviewWindowBuilder};

use crate::runtime_paths::RuntimePaths;

pub(crate) fn open_main_window(
    app: &tauri::App,
    paths: &RuntimePaths,
) -> Result<(), Box<dyn Error>> {
    let url = if cfg!(debug_assertions) {
        WebviewUrl::External(tauri::Url::parse(debug_startup_url())?)
    } else {
        WebviewUrl::App(release_startup_path().into())
    };

    WebviewWindowBuilder::new(app, "main", url)
        .data_directory(webview_data_directory(&paths.data_root))
        .title(product_window_title())
        .inner_size(1280.0, 800.0)
        .min_inner_size(1024.0, 680.0)
        .build()?;

    Ok(())
}

fn debug_startup_url() -> &'static str {
    "http://127.0.0.1:5173/#/dashboard"
}

fn release_startup_path() -> &'static str {
    "index.html#/dashboard"
}

fn product_window_title() -> &'static str {
    match option_env!("EXPORTDOCMANAGER_PRODUCT_EDITION") {
        Some("Document") => "出口单证管理系统（单证员版）",
        Some("Sales") => "外贸业务管理系统（业务员版）",
        _ => "外贸业务综合管理系统（全功能版）",
    }
}

fn webview_data_directory(data_root: &Path) -> PathBuf {
    data_root.join("WebView")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_startup_url_has_no_runtime_query() {
        assert_eq!(debug_startup_url(), "http://127.0.0.1:5173/#/dashboard");
        assert!(!debug_startup_url().contains("apiBaseUrl"));
        assert!(!debug_startup_url().contains("desktopAccessToken"));
    }

    #[test]
    fn release_startup_path_has_no_runtime_query() {
        assert_eq!(release_startup_path(), "index.html#/dashboard");
        assert!(!release_startup_path().contains("apiBaseUrl"));
        assert!(!release_startup_path().contains("desktopAccessToken"));
    }

    #[test]
    fn window_title_matches_compiled_product_edition() {
        let title = product_window_title();
        assert!(title.contains("版）"));
        assert!(title.contains("管理系统"));
    }

    #[test]
    fn webview_data_stays_under_runtime_data_root() {
        let data_root = PathBuf::from("D:/ExportDocManagerData");

        assert_eq!(
            webview_data_directory(&data_root),
            data_root.join("WebView")
        );
    }
}
