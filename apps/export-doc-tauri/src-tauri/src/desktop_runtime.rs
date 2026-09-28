//! Tauri lifecycle and IPC adapter; it contains no business dispatch or SQL.
use export_doc_engine::{engine::NativeService, paths::RuntimePaths};
use export_doc_server::desktop::DesktopHost;
use std::sync::{
    Mutex,
    atomic::{AtomicU8, Ordering},
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
    service: std::sync::Weak<NativeService>,
    context: DesktopRuntimeContext,
    host: Mutex<Option<DesktopHost>>,
    shutdown: ShutdownState,
}

#[derive(Default)]
struct ShutdownState(AtomicU8);

#[derive(Debug, PartialEq)]
enum ShutdownAction {
    Start,
    Wait,
    Exit,
}

impl ShutdownState {
    fn request(&self) -> ShutdownAction {
        match self
            .0
            .compare_exchange(0, 1, Ordering::AcqRel, Ordering::Acquire)
        {
            Ok(_) => ShutdownAction::Start,
            Err(1) => ShutdownAction::Wait,
            Err(_) => ShutdownAction::Exit,
        }
    }

    fn is_stopping(&self) -> bool {
        self.0.load(Ordering::Acquire) != 0
    }

    fn complete(&self) {
        self.0.store(2, Ordering::Release);
    }
}

pub(crate) fn start(
    paths: &crate::runtime_paths::RuntimePaths,
) -> Result<DesktopRuntime, Box<dyn std::error::Error>> {
    let paths = RuntimePaths::server(&paths.app_root, &paths.data_root)?;
    let retention = export_doc_engine::engine::tasks::retention::Retention::from_lookup(|key| {
        std::env::var(key).ok()
    })?;
    let service = NativeService::open_with_retention(paths, retention)?;
    let host = DesktopHost::start(service.clone(), !cfg!(feature = "custom-protocol"))?;
    Ok(DesktopRuntime {
        service: std::sync::Arc::downgrade(&service),
        context: DesktopRuntimeContext {
            api_base_url: host.api_base_url.clone(),
            desktop_access_token: host.access_token.clone(),
            product_edition: env!("EXPORTDOCMANAGER_PRODUCT_EDITION"),
            platform: std::env::consts::OS,
            single_window_station_capable: cfg!(target_os = "windows"),
        },
        host: Mutex::new(Some(host)),
        shutdown: ShutdownState::default(),
    })
}
pub(crate) async fn authorize_update(app: &tauri::AppHandle, token: String) -> Result<(), String> {
    let state = app.state::<DesktopRuntime>();
    if state.shutdown.is_stopping() {
        return Err("程序正在关闭。".into());
    }
    let service = state
        .service
        .upgrade()
        .ok_or_else(|| "后端已关闭。".to_owned())?;
    tauri::async_runtime::spawn_blocking(move || {
        service
            .authorize_administrator(&token)
            .map_err(|cause| cause.to_string())
    })
    .await
    .map_err(|_| "账号验证任务失败。".to_owned())?
}

#[tauri::command]
pub(crate) fn get_desktop_runtime_context(
    state: tauri::State<'_, DesktopRuntime>,
) -> DesktopRuntimeContext {
    state.context.clone()
}

pub(crate) fn stop(app: &tauri::AppHandle) -> Result<(), String> {
    if let Some(state) = app.try_state::<DesktopRuntime>() {
        state.shutdown.request();
        // Keep concurrent exit/update requests waiting for the same cleanup.
        let result = (|| {
            let mut host = state
                .host
                .lock()
                .map_err(|_| "后端生命周期状态异常。".to_owned())?;
            host.take().map_or(Ok(()), DesktopHost::stop)
        })();
        state.shutdown.complete();
        result?;
    }
    Ok(())
}

pub(crate) fn begin_graceful_shutdown(app: &tauri::AppHandle) -> bool {
    let Some(state) = app.try_state::<DesktopRuntime>() else {
        return false;
    };
    match state.shutdown.request() {
        ShutdownAction::Wait => return true,
        ShutdownAction::Exit => return false,
        ShutdownAction::Start => {}
    }
    let app = app.clone();
    std::thread::spawn(move || {
        let mut code = match stop(&app) {
            Ok(()) => 0,
            Err(error) => {
                crate::write_tauri_error(&error);
                1
            }
        };
        // Queue destruction before exit: wry closes each platform WebView controller.
        // Do not rely on Rust Drop after App::run, which exits the process directly.
        for window in app.webview_windows().values() {
            if let Err(error) = window.destroy() {
                crate::write_tauri_error(&format!("关闭 WebView 窗口失败：{error}"));
                code = 1;
            }
        }
        app.exit(code);
    });
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn repeated_exit_requests_wait_until_cleanup_finishes() {
        let state = ShutdownState::default();
        assert!(!state.is_stopping());
        assert_eq!(state.request(), ShutdownAction::Start);
        assert!(state.is_stopping());
        for _ in 0..10 {
            assert_eq!(state.request(), ShutdownAction::Wait);
        }
        state.complete();
        assert_eq!(state.request(), ShutdownAction::Exit);
        assert_eq!(state.request(), ShutdownAction::Exit);
    }

    #[test]
    fn concurrent_requests_start_only_one_cleanup_worker() {
        let state = ShutdownState::default();
        let actions = std::thread::scope(|scope| {
            let workers: Vec<_> = (0..8).map(|_| scope.spawn(|| state.request())).collect();
            workers
                .into_iter()
                .map(|worker| worker.join().unwrap())
                .collect::<Vec<_>>()
        });
        assert_eq!(
            actions
                .iter()
                .filter(|action| **action == ShutdownAction::Start)
                .count(),
            1
        );
        assert_eq!(
            actions
                .iter()
                .filter(|action| **action == ShutdownAction::Wait)
                .count(),
            7
        );
        state.complete();
        assert_eq!(state.request(), ShutdownAction::Exit);
    }
}
