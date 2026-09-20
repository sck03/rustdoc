//! Tauri lifecycle and IPC adapter; it contains no business dispatch or SQL.
use export_doc_engine::{engine::NativeService, paths::RuntimePaths};
use export_doc_server::desktop::DesktopHost;
use std::sync::{
    Mutex,
    atomic::{AtomicBool, Ordering},
};
use tauri::Manager;

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DesktopRuntimeContext {
    api_base_url: String,
    desktop_access_token: String,
    product_edition: &'static str,
    platform: &'static str,
    single_window_station_capable: bool,
}

pub(crate) struct DesktopRuntime {
    context: DesktopRuntimeContext,
    host: Mutex<Option<DesktopHost>>,
    stopping: AtomicBool,
}

pub(crate) fn start(
    paths: &crate::runtime_paths::RuntimePaths,
) -> Result<DesktopRuntime, Box<dyn std::error::Error>> {
    let paths = RuntimePaths::server(&paths.app_root, &paths.data_root)?;
    let retention = export_doc_engine::engine::tasks::retention::Retention::from_lookup(|key| {
        std::env::var(key).ok()
    })?;
    let service = NativeService::open_with_retention(paths, retention)?;
    let host = DesktopHost::start(service, !cfg!(feature = "custom-protocol"))?;
    Ok(DesktopRuntime {
        context: DesktopRuntimeContext {
            api_base_url: host.api_base_url.clone(),
            desktop_access_token: host.access_token.clone(),
            product_edition: env!("EXPORTDOCMANAGER_PRODUCT_EDITION"),
            platform: std::env::consts::OS,
            single_window_station_capable: cfg!(target_os = "windows"),
        },
        host: Mutex::new(Some(host)),
        stopping: AtomicBool::new(false),
    })
}

#[tauri::command]
pub(crate) fn get_desktop_runtime_context(
    state: tauri::State<'_, DesktopRuntime>,
) -> DesktopRuntimeContext {
    state.context.clone()
}

pub(crate) fn stop(app: &tauri::AppHandle) -> Result<(), String> {
    if let Some(state) = app.try_state::<DesktopRuntime>() {
        let host = state
            .host
            .lock()
            .map_err(|_| "后端生命周期状态异常。".to_owned())?
            .take();
        if let Some(host) = host {
            host.stop()?;
        }
    }
    Ok(())
}

pub(crate) fn begin_graceful_shutdown(app: &tauri::AppHandle) -> bool {
    let Some(state) = app.try_state::<DesktopRuntime>() else {
        return false;
    };
    if state.stopping.swap(true, Ordering::AcqRel) {
        return false;
    }
    let app = app.clone();
    std::thread::spawn(move || {
        let code = match stop(&app) {
            Ok(()) => 0,
            Err(error) => {
                crate::write_tauri_error(&error);
                1
            }
        };
        app.exit(code);
    });
    true
}
