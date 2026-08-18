use std::sync::{
    atomic::{AtomicU8, Ordering},
    Arc, Mutex,
};

use serde::Serialize;
use tauri::{Emitter, Manager};
use tauri_plugin_updater::{Updater, UpdaterExt};
use url::Url;

use crate::{desktop_commands, runtime_paths::RuntimePaths, sidecar};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TauriUpdaterCheckResult {
    supported: bool,
    install_supported: bool,
    configured: bool,
    update_available: bool,
    current_version: String,
    latest_version: String,
    target: String,
    download_url: String,
    body: String,
    date: String,
    status_text: String,
    error_message: String,
    storage_policy: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TauriUpdaterInstallResult {
    success: bool,
    installed_version: String,
    status_text: String,
    restart_policy: String,
    storage_policy: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TauriUpdaterCancelResult {
    accepted: bool,
    status_text: String,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct TauriUpdaterProgress {
    phase: &'static str,
    downloaded_bytes: u64,
    total_bytes: Option<u64>,
    progress_percent: Option<u8>,
    status_text: &'static str,
}

#[derive(Default)]
pub(crate) struct TauriUpdaterState {
    active: Mutex<Option<Arc<UpdaterCancellation>>>,
}

struct UpdaterCancellation {
    phase: AtomicU8,
    abort: Mutex<Option<Box<dyn Fn() + Send + Sync>>>,
}

impl UpdaterCancellation {
    const DOWNLOADING: u8 = 0;
    const CANCEL_REQUESTED: u8 = 1;
    const INSTALLING: u8 = 2;

    fn new() -> Self {
        Self {
            phase: AtomicU8::new(Self::DOWNLOADING),
            abort: Mutex::new(None),
        }
    }

    fn request_cancel(&self) -> bool {
        if self
            .phase
            .compare_exchange(
                Self::DOWNLOADING,
                Self::CANCEL_REQUESTED,
                Ordering::AcqRel,
                Ordering::Acquire,
            )
            .is_err()
        {
            return false;
        }

        if let Some(abort) = self
            .abort
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .take()
        {
            abort();
        }
        true
    }

    fn set_abort(&self, abort: impl Fn() + Send + Sync + 'static) {
        let abort: Box<dyn Fn() + Send + Sync> = Box::new(abort);
        if self.phase.load(Ordering::Acquire) == Self::CANCEL_REQUESTED {
            abort();
            return;
        }

        let mut slot = self.abort.lock().unwrap_or_else(|error| error.into_inner());
        if self.phase.load(Ordering::Acquire) == Self::CANCEL_REQUESTED {
            drop(slot);
            abort();
        } else {
            *slot = Some(abort);
        }
    }

    fn begin_install(&self) -> bool {
        self.phase
            .compare_exchange(
                Self::DOWNLOADING,
                Self::INSTALLING,
                Ordering::AcqRel,
                Ordering::Acquire,
            )
            .is_ok()
    }

    fn is_cancel_requested(&self) -> bool {
        self.phase.load(Ordering::Acquire) == Self::CANCEL_REQUESTED
    }
}

impl TauriUpdaterState {
    fn begin(&self) -> Result<Arc<UpdaterCancellation>, String> {
        let mut active = self
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if active.is_some() {
            return Err("已有软件更新任务正在运行。".to_owned());
        }

        let cancellation = Arc::new(UpdaterCancellation::new());
        *active = Some(cancellation.clone());
        Ok(cancellation)
    }

    fn finish(&self, cancellation: &Arc<UpdaterCancellation>) {
        let mut active = self
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if active
            .as_ref()
            .is_some_and(|current| Arc::ptr_eq(current, cancellation))
        {
            *active = None;
        }
    }

    fn request_cancel(&self) -> bool {
        let cancellation = self
            .active
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone();
        cancellation.is_some_and(|value| value.request_cancel())
    }
}

const TAURI_UPDATER_STORAGE_POLICY: &str =
    "软件更新只更新程序文件；业务数据库、授权文件和运行数据保持在运行数据目录。";
const PORTABLE_UPDATER_STORAGE_POLICY: &str =
    "便携版不执行安装器式自动更新；请退出程序后替换程序文件，并保留解包目录旁的 App_Data。";
const MAX_UPDATER_ENDPOINT_LENGTH: usize = 2048;
const TAURI_UPDATER_PROGRESS_EVENT: &str = "exportdoc://updater-progress";

#[tauri::command]
pub(crate) async fn check_tauri_update(
    app: tauri::AppHandle,
    endpoint: Option<String>,
) -> Result<TauriUpdaterCheckResult, String> {
    let portable = app.state::<RuntimePaths>().portable;
    let updater = build_tauri_updater(&app, endpoint)?;
    match updater.check().await.map_err(describe_updater_error)? {
        Some(update) => Ok(TauriUpdaterCheckResult {
            supported: true,
            install_supported: !portable,
            configured: true,
            update_available: true,
            current_version: update.current_version,
            latest_version: update.version,
            target: update.target,
            download_url: update.download_url.to_string(),
            body: update.body.unwrap_or_default(),
            date: update
                .date
                .map(|value| value.to_string())
                .unwrap_or_default(),
            status_text: if portable {
                "发现新版本。便携版请下载新的绿色便携包，退出程序后替换程序文件并保留 App_Data。"
                    .to_owned()
            } else {
                "发现可安装的新版本。".to_owned()
            },
            error_message: String::new(),
            storage_policy: updater_storage_policy(portable).to_owned(),
        }),
        None => {
            let version = app.package_info().version.to_string();
            Ok(TauriUpdaterCheckResult {
                supported: true,
                install_supported: !portable,
                configured: true,
                update_available: false,
                current_version: version.clone(),
                latest_version: version,
                target: String::new(),
                download_url: String::new(),
                body: String::new(),
                date: String::new(),
                status_text: "检查完成，当前已是最新版本。".to_owned(),
                error_message: String::new(),
                storage_policy: updater_storage_policy(portable).to_owned(),
            })
        }
    }
}

#[tauri::command]
pub(crate) async fn install_tauri_update(
    app: tauri::AppHandle,
    endpoint: Option<String>,
    state: tauri::State<'_, TauriUpdaterState>,
) -> Result<TauriUpdaterInstallResult, String> {
    let cancellation = state.begin()?;
    let result = install_tauri_update_core(app, endpoint, cancellation.clone()).await;
    state.finish(&cancellation);
    result
}

#[tauri::command]
pub(crate) fn cancel_tauri_update(
    state: tauri::State<'_, TauriUpdaterState>,
) -> TauriUpdaterCancelResult {
    let accepted = state.request_cancel();
    TauriUpdaterCancelResult {
        accepted,
        status_text: if accepted {
            "已请求取消更新下载，正在停止网络传输。".to_owned()
        } else {
            "当前没有可取消的更新下载；进入安装阶段后不能中断。".to_owned()
        },
    }
}

async fn install_tauri_update_core(
    app: tauri::AppHandle,
    endpoint: Option<String>,
    cancellation: Arc<UpdaterCancellation>,
) -> Result<TauriUpdaterInstallResult, String> {
    ensure_updater_install_supported(app.state::<RuntimePaths>().portable)?;
    let updater = build_tauri_updater(&app, endpoint)?;
    let update = updater
        .check()
        .await
        .map_err(describe_updater_error)?
        .ok_or_else(|| "未发现可安装的新版本。".to_owned())?;
    let version = update.version.clone();

    emit_updater_progress(&app, "preparing", 0, None, "正在准备更新下载。");
    let download_app = app.clone();
    let install_app = app.clone();
    let download_update = update.clone();
    let download_handle = tauri::async_runtime::spawn(async move {
        let mut downloaded_bytes = 0_u64;
        download_update
            .download(
                move |chunk_bytes, total_bytes| {
                    downloaded_bytes = downloaded_bytes.saturating_add(chunk_bytes as u64);
                    emit_updater_progress(
                        &download_app,
                        "downloading",
                        downloaded_bytes,
                        total_bytes,
                        "正在下载并校验更新包。",
                    );
                },
                move || {
                    emit_updater_progress(
                        &install_app,
                        "verifying",
                        0,
                        None,
                        "下载完成，正在校验更新包签名。",
                    );
                },
            )
            .await
    });
    let abort_handle = download_handle.inner().abort_handle();
    cancellation.set_abort(move || abort_handle.abort());
    let bytes = match download_handle.await {
        Ok(result) => result.map_err(describe_updater_error)?,
        Err(_error) if cancellation.is_cancel_requested() => {
            emit_updater_progress(&app, "canceled", 0, None, "更新下载已取消。");
            return Err("软件更新下载已取消。".to_owned());
        }
        Err(error) => return Err(format!("软件更新下载任务失败：{error}")),
    };

    if !cancellation.begin_install() {
        emit_updater_progress(&app, "canceled", 0, None, "更新下载已取消。");
        return Err("软件更新下载已取消。".to_owned());
    }

    emit_updater_progress(&app, "installing", 0, None, "签名校验完成，正在安装更新。");
    update.install(bytes).map_err(describe_updater_error)?;

    emit_updater_progress(&app, "restarting", 0, None, "更新安装完成，正在重启程序。");

    desktop_commands::confirm_app_exit();
    sidecar::run_shutdown_maintenance(&app);
    sidecar::stop_sidecar(&app);
    app.request_restart();

    Ok(TauriUpdaterInstallResult {
        success: true,
        installed_version: version,
        status_text: "更新已安装，正在重启。".to_owned(),
        restart_policy: "安装完成后自动重启程序。".to_owned(),
        storage_policy: TAURI_UPDATER_STORAGE_POLICY.to_owned(),
    })
}

fn emit_updater_progress(
    app: &tauri::AppHandle,
    phase: &'static str,
    downloaded_bytes: u64,
    total_bytes: Option<u64>,
    status_text: &'static str,
) {
    let progress_percent = total_bytes
        .filter(|total| *total > 0)
        .map(|total| ((downloaded_bytes.saturating_mul(100) / total).min(100)) as u8);
    let _ = app.emit(
        TAURI_UPDATER_PROGRESS_EVENT,
        TauriUpdaterProgress {
            phase,
            downloaded_bytes,
            total_bytes,
            progress_percent,
            status_text,
        },
    );
}

fn ensure_updater_install_supported(portable: bool) -> Result<(), String> {
    if portable {
        return Err(
            "绿色便携版不会启动系统安装器。请下载新的便携包，退出程序后替换程序文件，并保留原目录中的 App_Data。"
                .to_owned(),
        );
    }
    Ok(())
}

fn updater_storage_policy(portable: bool) -> &'static str {
    if portable {
        PORTABLE_UPDATER_STORAGE_POLICY
    } else {
        TAURI_UPDATER_STORAGE_POLICY
    }
}

fn build_tauri_updater(
    app: &tauri::AppHandle,
    endpoint: Option<String>,
) -> Result<Updater, String> {
    let mut builder = app.updater_builder();
    if let Some(endpoint) = parse_runtime_endpoint(endpoint)? {
        builder = builder
            .endpoints(vec![endpoint])
            .map_err(describe_updater_error)?;
    }

    let app_for_exit = app.clone();
    builder = builder.on_before_exit(move || {
        desktop_commands::confirm_app_exit();
        sidecar::run_shutdown_maintenance(&app_for_exit);
        sidecar::stop_sidecar(&app_for_exit);
    });

    builder.build().map_err(describe_updater_error)
}

fn parse_runtime_endpoint(endpoint: Option<String>) -> Result<Option<Url>, String> {
    let Some(value) = endpoint else {
        return Ok(None);
    };
    let normalized = value.trim();
    if normalized.is_empty() {
        return Ok(None);
    }
    if normalized.len() > MAX_UPDATER_ENDPOINT_LENGTH {
        return Err(format!(
            "软件更新地址不能超过 {MAX_UPDATER_ENDPOINT_LENGTH} 个字符。"
        ));
    }
    if normalized.chars().any(char::is_control) || normalized.contains('\\') {
        return Err("软件更新地址包含不允许的控制字符或反斜杠。".to_owned());
    }

    let parsed = Url::parse(normalized)
        .map_err(|_| "软件更新地址必须是完整的 http:// 或 https:// 绝对地址。".to_owned())?;
    if parsed.scheme() != "http" && parsed.scheme() != "https" {
        return Err("软件更新地址只支持 http:// 或 https://。".to_owned());
    }
    if parsed.host_str().is_none() {
        return Err("软件更新地址必须包含服务器主机名或 IP 地址。".to_owned());
    }
    if !parsed.username().is_empty() || parsed.password().is_some() {
        return Err(
            "软件更新地址不能包含用户名或密码；需要鉴权时应由受控更新网关处理。".to_owned(),
        );
    }
    if parsed.fragment().is_some() {
        return Err("软件更新地址不能包含 # 片段。".to_owned());
    }

    Ok(Some(parsed))
}

fn describe_updater_error(error: tauri_plugin_updater::Error) -> String {
    let message = error.to_string();
    if message.contains("empty endpoints")
        || message.contains("Updater endpoints are empty")
        || message.contains("does not have any endpoints")
    {
        return "当前安装包和管理员设置均未配置更新地址。请由管理员在系统设置中填写 GitHub、自建服务器或企业内网更新地址。".to_owned();
    }

    if message.contains("secure protocol") || message.contains("InsecureTransportProtocol") {
        return "当前安装包未启用公司内网 HTTP 更新能力；请改用 HTTPS 更新地址，或重新发布支持受控内网 HTTP 的签名安装包。".to_owned();
    }

    if message.contains("signature") || message.contains("pubkey") || message.contains("public key")
    {
        return format!("更新签名校验失败或安装包未内置签名公钥: {message}");
    }

    if message.contains("request")
        || message.contains("connect")
        || message.contains("timed out")
        || message.contains("status code")
    {
        return format!("更新地址不可达或更新清单响应无效: {message}");
    }

    format!("软件更新执行失败: {message}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn runtime_endpoint_accepts_https_and_trusted_network_http() {
        let https = parse_runtime_endpoint(Some(
            " https://github.com/sck03/rustdoc/releases/latest/download/latest.json ".to_owned(),
        ))
        .unwrap()
        .unwrap();
        assert_eq!(https.scheme(), "https");

        let http = parse_runtime_endpoint(Some(
            "http://updates.internal:8080/desktop/latest.json".to_owned(),
        ))
        .unwrap()
        .unwrap();
        assert_eq!(http.scheme(), "http");
    }

    #[test]
    fn runtime_endpoint_preserves_supported_tauri_placeholders() {
        let endpoint = parse_runtime_endpoint(Some(
            "https://updates.example.test/{{target}}/{{arch}}/{{current_version}}".to_owned(),
        ))
        .unwrap()
        .unwrap();

        assert!(endpoint.as_str().contains("%7B%7Btarget%7D%7D"));
        assert!(endpoint.as_str().contains("%7B%7Bcurrent_version%7D%7D"));
    }

    #[test]
    fn runtime_endpoint_empty_value_uses_packaged_default() {
        assert!(parse_runtime_endpoint(None).unwrap().is_none());
        assert!(parse_runtime_endpoint(Some("   ".to_owned()))
            .unwrap()
            .is_none());
    }

    #[test]
    fn runtime_endpoint_rejects_unsafe_or_unsupported_values() {
        for endpoint in [
            "file:///tmp/latest.json",
            "ftp://updates.example.test/latest.json",
            "https://user:password@updates.example.test/latest.json",
            "https://updates.example.test/latest.json#unsigned-fragment",
            "https:\\updates.example.test\\latest.json",
        ] {
            assert!(
                parse_runtime_endpoint(Some(endpoint.to_owned())).is_err(),
                "endpoint should be rejected: {endpoint}"
            );
        }
    }

    #[test]
    fn portable_runtime_rejects_installer_style_updates() {
        assert!(ensure_updater_install_supported(false).is_ok());
        let error = ensure_updater_install_supported(true).unwrap_err();
        assert!(error.contains("绿色便携版"));
        assert!(error.contains("App_Data"));
        assert_eq!(
            updater_storage_policy(true),
            PORTABLE_UPDATER_STORAGE_POLICY
        );
    }

    #[test]
    fn updater_cancellation_is_only_accepted_before_installation() {
        let canceled = UpdaterCancellation::new();
        assert!(canceled.request_cancel());
        assert!(!canceled.request_cancel());
        assert!(!canceled.begin_install());

        let installing = UpdaterCancellation::new();
        assert!(installing.begin_install());
        assert!(!installing.request_cancel());
    }
}
