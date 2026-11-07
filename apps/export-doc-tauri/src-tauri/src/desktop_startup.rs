//! Startup window and worker coordination; filesystem work stays in the migration modules.
use crate::{
    migration_progress::{MigrationProgress, Snapshot},
    runtime_paths::{RuntimePathSetup, RuntimePaths},
};
use std::{error::Error, sync::Arc};
use tauri::{Manager, WebviewUrl, WebviewWindowBuilder};

const WINDOW: &str = "migration-startup";

pub(crate) fn prepare(app: &tauri::App) -> Result<(), Box<dyn Error>> {
    let setup = RuntimePathSetup::resolve(app)?;
    if !setup.migration_pending()? {
        return start(app.handle(), setup.prepare(&MigrationProgress::default())?);
    }
    // The data roots must remain closed while their WebView profiles are copied.
    let profile = crate::runtime_data_root_migration::startup_profile(&setup.runtime_config_root)?;
    crate::runtime_paths::ensure_directory(&profile)?;
    let progress = Arc::new(MigrationProgress::default());
    progress.stage("prepare", None);
    app.manage(progress.clone());
    let url = if cfg!(feature = "custom-protocol") {
        WebviewUrl::App("index.html#/startup-migration".into())
    } else {
        WebviewUrl::External(tauri::Url::parse(
            "http://127.0.0.1:5173/#/startup-migration",
        )?)
    };
    WebviewWindowBuilder::new(app, WINDOW, url)
        .on_navigation(crate::window::allow_main_navigation)
        .data_directory(profile)
        .title("数据目录迁移")
        .inner_size(640.0, 400.0)
        .min_inner_size(480.0, 320.0)
        .build()?;
    let app = app.handle().clone();
    std::thread::Builder::new()
        .name("desktop-migration".into())
        .spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let paths = setup
                    .prepare(&progress)
                    .map_err(|error| error.to_string())?;
                progress.check().map_err(|error| error.to_string())?;
                progress.stage("starting", None);
                start(&app, paths).map_err(|error| error.to_string())
            }))
            .unwrap_or_else(|_| Err("迁移初始化异常，恢复标记已保留，请关闭后重新启动。".into()));
            match result {
                Ok(()) => {
                    progress.stage("ready", None);
                    if progress.is_cancelled() {
                        crate::desktop_commands::confirm_app_exit();
                        if !crate::desktop_runtime::begin_graceful_shutdown(&app) {
                            app.exit(0);
                        }
                        return;
                    }
                    if let Some(window) = app.get_webview_window(WINDOW) {
                        let _ = window.destroy();
                    }
                }
                Err(error) => {
                    crate::write_tauri_error(&error);
                    progress.fail(error);
                    if progress.is_cancelled() {
                        app.exit(0);
                    }
                }
            }
        })?;
    Ok(())
}

fn start(app: &tauri::AppHandle, paths: RuntimePaths) -> Result<(), Box<dyn Error>> {
    crate::set_runtime_diagnostic_log_root(&paths.log_root);
    app.manage(paths.clone());
    app.manage(crate::tauri_updater_commands::TauriUpdaterState::default());
    match crate::runtime_prerequisites::check(&paths).and_then(|_| {
        crate::desktop_runtime::start(&paths, app.clone()).map_err(|error| error.to_string())
    }) {
        Ok(runtime) => {
            app.manage(runtime);
        }
        Err(message) => {
            let log = crate::write_tauri_error(&message)
                .unwrap_or_else(|| paths.log_root.join("tauri-errors.log"));
            app.manage(crate::desktop_runtime::StartupFailure {
                message,
                log_path: log.to_string_lossy().into_owned(),
            });
        }
    }
    crate::window::open_main_window(app, &paths)?;
    crate::STARTUP_FINISHED.store(true, std::sync::atomic::Ordering::Release);
    Ok(())
}

/// Closing requests cancellation; the worker reaches a safe I/O boundary first.
pub(crate) fn cancel_if_running(app: &tauri::AppHandle) -> bool {
    let Some(progress) = app.try_state::<Arc<MigrationProgress>>() else {
        return false;
    };
    let snapshot = progress.snapshot();
    if snapshot.error.is_some() || snapshot.stage == "ready" {
        return false;
    }
    progress.cancel();
    true
}

#[tauri::command]
pub(crate) fn get_data_root_migration_progress(
    window: tauri::WebviewWindow,
) -> Result<Snapshot, String> {
    if window.label() != WINDOW {
        return Err("此窗口不能读取启动迁移状态。".into());
    }
    window
        .app_handle()
        .try_state::<Arc<MigrationProgress>>()
        .map(|progress| progress.snapshot())
        .ok_or_else(|| "当前没有启动迁移。".into())
}
