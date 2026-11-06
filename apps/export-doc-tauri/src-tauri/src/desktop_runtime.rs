//! Tauri lifecycle and IPC adapter; it contains no business dispatch or SQL.
use export_doc_engine::{engine::NativeService, paths::RuntimePaths};
use export_doc_server::desktop::DesktopHost;
use std::sync::{
    Mutex,
    atomic::{AtomicBool, AtomicU8, Ordering},
};
use tauri::{Emitter, Manager};

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DesktopRuntimeContext {
    api_base_url: String,
    desktop_access_token: String,
    product_edition: &'static str,
    platform: &'static str,
    single_window_station_capable: bool,
    startup_error: Option<String>,
    log_path: String,
}
pub(crate) struct StartupFailure {
    pub(crate) message: String,
    pub(crate) log_path: String,
}

pub(crate) struct DesktopRuntime {
    service: std::sync::Weak<NativeService>,
    context: DesktopRuntimeContext,
    host: Mutex<HostState>,
    shutdown: ShutdownState,
    failed: std::sync::Arc<AtomicBool>,
}
struct HostState {
    host: Option<DesktopHost>,
    result: Option<Result<(), String>>,
}
impl HostState {
    fn stop(
        &mut self,
        close: impl FnOnce(Option<DesktopHost>) -> Result<(), String>,
    ) -> Result<(), String> {
        if let Some(result) = &self.result {
            return result.clone();
        }
        let result = close(self.host.take());
        self.result = Some(result.clone());
        result
    }
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
    app: tauri::AppHandle,
) -> Result<DesktopRuntime, Box<dyn std::error::Error>> {
    let paths = RuntimePaths::server(&paths.app_root, &paths.data_root)?;
    let log_path = paths
        .log_root
        .join("tauri-errors.log")
        .to_string_lossy()
        .into_owned();
    let retention = export_doc_engine::engine::tasks::retention::Retention::from_lookup(|key| {
        std::env::var(key).ok()
    })?;
    let service =
        NativeService::open_desktop(paths, retention, env!("EXPORTDOCMANAGER_PRODUCT_EDITION"))?;
    let host = DesktopHost::start(
        service.clone(),
        !cfg!(feature = "custom-protocol"),
        move |message| {
            crate::write_tauri_error(message);
            let _ = app.emit("exportdoc://backend-stopped", message);
        },
    )?;
    let failed = host.failed.clone();
    Ok(DesktopRuntime {
        service: std::sync::Arc::downgrade(&service),
        context: DesktopRuntimeContext {
            api_base_url: host.api_base_url.clone(),
            desktop_access_token: host.access_token.clone(),
            product_edition: env!("EXPORTDOCMANAGER_PRODUCT_EDITION"),
            startup_error: None,
            log_path,
            platform: std::env::consts::OS,
            single_window_station_capable: cfg!(target_os = "windows"),
        },
        host: Mutex::new(HostState {
            host: Some(host),
            result: None,
        }),
        shutdown: ShutdownState::default(),
        failed,
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
    app: tauri::AppHandle,
) -> Result<DesktopRuntimeContext, String> {
    if let Some(state) = app.try_state::<DesktopRuntime>() {
        let mut context = state.context.clone();
        if state.failed.load(Ordering::Acquire) {
            context.startup_error = Some("桌面后端异常停止，请重启程序。".into());
        }
        return Ok(context);
    }
    let failure = app
        .try_state::<StartupFailure>()
        .ok_or_else(|| "桌面服务尚未就绪。".to_owned())?;
    Ok(DesktopRuntimeContext {
        api_base_url: String::new(),
        desktop_access_token: String::new(),
        product_edition: env!("EXPORTDOCMANAGER_PRODUCT_EDITION"),
        platform: std::env::consts::OS,
        single_window_station_capable: false,
        startup_error: Some(failure.message.clone()),
        log_path: failure.log_path.clone(),
    })
}
#[tauri::command]
pub(crate) fn restart_after_startup_failure(app: tauri::AppHandle) -> Result<(), String> {
    if app.try_state::<StartupFailure>().is_none()
        && !app
            .try_state::<DesktopRuntime>()
            .is_some_and(|state| state.failed.load(Ordering::Acquire))
    {
        return Err("当前程序不在启动失败状态。".into());
    }
    crate::desktop_commands::confirm_app_exit();
    app.request_restart();
    Ok(())
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
            host.stop(|host| host.map_or(Ok(()), DesktopHost::stop))
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
    fn overlapping_stop_retains_failure_and_does_not_report_later_success() {
        let host = Mutex::new(HostState {
            host: None,
            result: None,
        });
        let first = host
            .lock()
            .unwrap()
            .stop(|_| Err("injected stop timeout".into()));
        assert!(first.is_err());
        assert_eq!(
            host.lock()
                .unwrap()
                .stop(|_| panic!("must not retry consumed host")),
            first
        );
    }

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
