use std::{
    error::Error,
    fs,
    process::{Child, Command, Stdio},
    sync::{
        atomic::{AtomicBool, Ordering},
        Mutex,
    },
    thread,
    time::Duration,
};

use tauri::Manager;

use crate::{runtime_paths::RuntimePaths, sidecar_endpoint, sidecar_process, sidecar_shutdown};

pub(crate) const DESKTOP_ACCESS_TOKEN_ENV: &str = "EXPORTDOCMANAGER_DESKTOP_TOKEN";

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct DesktopRuntimeContext {
    api_base_url: String,
    desktop_access_token: String,
    product_edition: &'static str,
    platform: &'static str,
    single_window_station_capable: bool,
}

pub(crate) struct SidecarState {
    child: Mutex<Option<Child>>,
    context: DesktopRuntimeContext,
    shutdown_maintenance_started: AtomicBool,
}

impl SidecarState {
    pub(crate) fn new(child: Child, api_base_url: String, desktop_access_token: String) -> Self {
        Self {
            child: Mutex::new(Some(child)),
            context: DesktopRuntimeContext {
                api_base_url,
                desktop_access_token,
                product_edition: resolve_product_edition(),
                platform: std::env::consts::OS,
                single_window_station_capable: cfg!(target_os = "windows"),
            },
            shutdown_maintenance_started: AtomicBool::new(false),
        }
    }

    fn runtime_context(&self) -> DesktopRuntimeContext {
        self.context.clone()
    }

    fn try_begin_shutdown_maintenance(&self) -> bool {
        !self
            .shutdown_maintenance_started
            .swap(true, Ordering::AcqRel)
    }
}

pub(crate) struct SidecarLaunch {
    pub(crate) api_base_url: String,
    pub(crate) desktop_access_token: String,
    pub(crate) child: Child,
}

pub(crate) fn start_sidecar(paths: &RuntimePaths) -> Result<SidecarLaunch, Box<dyn Error>> {
    let listen_url = "http://127.0.0.1:0";
    let desktop_access_token = sidecar_endpoint::resolve_desktop_access_token()?;
    let endpoint_file = sidecar_endpoint::create_sidecar_endpoint_file(paths)?;
    let stdout_log_path = paths.log_root.join("api-sidecar.stdout.log");
    let stderr_log_path = paths.log_root.join("api-sidecar.stderr.log");
    let mut stdout_log = crate::log_rotation::RotatingLogWriter::open(&stdout_log_path)?;
    let mut stderr_log = crate::log_rotation::RotatingLogWriter::open(&stderr_log_path)?;

    sidecar_process::write_sidecar_launch_marker(&mut stdout_log, listen_url, paths)?;
    sidecar_process::write_sidecar_launch_marker(&mut stderr_log, listen_url, paths)?;

    let mut command = Command::new(&paths.sidecar_path);
    command
        .arg("--urls")
        .arg(listen_url)
        .arg("--endpoint-file")
        .arg(&endpoint_file)
        .arg("--app-root")
        .arg(&paths.app_root)
        .arg("--data-root")
        .arg(&paths.data_root)
        .arg("--product-edition")
        .arg(resolve_product_edition())
        .env("EXPORTDOCMANAGER_DATA_ROOT", &paths.data_root)
        .env(DESKTOP_ACCESS_TOKEN_ENV, &desktop_access_token)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        const CREATE_NEW_PROCESS_GROUP: u32 = 0x00000200;
        command.creation_flags(CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP);
    }

    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        command.process_group(0);
    }

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(error) => {
            let _ = fs::remove_file(&endpoint_file);
            return Err(format!(
                "Failed to start API sidecar at '{}': {error}",
                paths.sidecar_path.display()
            )
            .into());
        }
    };

    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| "API sidecar stdout pipe was not created.".to_owned())?;
    let stderr = child
        .stderr
        .take()
        .ok_or_else(|| "API sidecar stderr pipe was not created.".to_owned())?;
    sidecar_process::spawn_log_pump(stdout, stdout_log, "stdout");
    sidecar_process::spawn_log_pump(stderr, stderr_log, "stderr");

    let api_base_url = sidecar_endpoint::wait_for_endpoint_and_health(
        &mut child,
        &endpoint_file,
        Duration::from_secs(20),
    );
    let _ = fs::remove_file(&endpoint_file);
    let api_base_url = match api_base_url {
        Ok(value) => value,
        Err(error) => {
            sidecar_process::terminate_child_tree(&mut child);
            return Err(format!(
                "{error}. See sidecar logs: '{}' and '{}'.",
                stdout_log_path.display(),
                stderr_log_path.display()
            )
            .into());
        }
    };

    Ok(SidecarLaunch {
        api_base_url,
        desktop_access_token,
        child,
    })
}

fn resolve_product_edition() -> &'static str {
    normalize_product_edition(option_env!("EXPORTDOCMANAGER_PRODUCT_EDITION"))
}

fn normalize_product_edition(value: Option<&str>) -> &'static str {
    match value {
        Some("Document") => "Document",
        Some("Sales") => "Sales",
        _ => "Full",
    }
}

pub(crate) fn stop_sidecar(app: &tauri::AppHandle) {
    if let Some(state) = app.try_state::<SidecarState>() {
        state
            .shutdown_maintenance_started
            .store(true, Ordering::Release);
        if let Ok(mut child) = state.child.lock() {
            if let Some(mut process) = child.take() {
                sidecar_process::stop_process(&mut process);
            }
        }
    }
}

pub(crate) fn begin_graceful_shutdown(app: &tauri::AppHandle) -> bool {
    let Some(state) = app.try_state::<SidecarState>() else {
        app.exit(0);
        return true;
    };
    if !state.try_begin_shutdown_maintenance() {
        return false;
    }

    let context = state.runtime_context();
    let app_handle = app.clone();
    thread::spawn(move || {
        if let Err(error) = sidecar_shutdown::post_shutdown_maintenance(
            &context.api_base_url,
            &context.desktop_access_token,
            Duration::from_secs(30),
        ) {
            eprintln!("Shutdown maintenance skipped or failed: {error}");
        }

        stop_sidecar(&app_handle);
        app_handle.exit(0);
    });
    true
}

/// Runs shutdown maintenance synchronously for the updater restart handshake.
pub(crate) fn run_shutdown_maintenance(app: &tauri::AppHandle) {
    let Some(state) = app.try_state::<SidecarState>() else {
        return;
    };
    if !state.try_begin_shutdown_maintenance() {
        return;
    }

    let context = state.runtime_context();
    if let Err(error) = sidecar_shutdown::post_shutdown_maintenance(
        &context.api_base_url,
        &context.desktop_access_token,
        Duration::from_secs(30),
    ) {
        eprintln!("Shutdown maintenance skipped or failed: {error}");
    }
}

#[tauri::command]
pub(crate) fn get_desktop_runtime_context(
    state: tauri::State<'_, SidecarState>,
) -> DesktopRuntimeContext {
    state.runtime_context()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalizes_product_edition_for_sidecar_launch() {
        assert_eq!(normalize_product_edition(Some("Document")), "Document");
        assert_eq!(normalize_product_edition(Some("Sales")), "Sales");
        assert_eq!(normalize_product_edition(Some("Full")), "Full");
        assert_eq!(normalize_product_edition(Some("unknown")), "Full");
        assert_eq!(normalize_product_edition(None), "Full");
    }
}
