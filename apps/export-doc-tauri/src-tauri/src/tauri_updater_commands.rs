use serde::Serialize;
use tauri_plugin_updater::{Updater, UpdaterExt};
use url::Url;

use crate::sidecar;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TauriUpdaterCheckResult {
    supported: bool,
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

const TAURI_UPDATER_STORAGE_POLICY: &str =
    "软件更新只更新程序文件；业务数据库、授权文件和运行数据保持在运行数据目录。";

#[tauri::command]
pub(crate) async fn check_tauri_update(
    app: tauri::AppHandle,
    endpoint: Option<String>,
    public_key: Option<String>,
) -> Result<TauriUpdaterCheckResult, String> {
    let updater = build_tauri_updater(&app, endpoint, public_key)?;
    match updater.check().await.map_err(describe_updater_error)? {
        Some(update) => Ok(TauriUpdaterCheckResult {
            supported: true,
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
            status_text: "发现可安装的新版本。".to_owned(),
            error_message: String::new(),
            storage_policy: TAURI_UPDATER_STORAGE_POLICY.to_owned(),
        }),
        None => {
            let version = app.package_info().version.to_string();
            Ok(TauriUpdaterCheckResult {
                supported: true,
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
                storage_policy: TAURI_UPDATER_STORAGE_POLICY.to_owned(),
            })
        }
    }
}

#[tauri::command]
pub(crate) async fn install_tauri_update(
    app: tauri::AppHandle,
    endpoint: Option<String>,
    public_key: Option<String>,
) -> Result<TauriUpdaterInstallResult, String> {
    let updater = build_tauri_updater(&app, endpoint, public_key)?;
    let update = updater
        .check()
        .await
        .map_err(describe_updater_error)?
        .ok_or_else(|| "未发现可安装的新版本。".to_owned())?;
    let version = update.version.clone();

    update
        .download_and_install(|_, _| {}, || {})
        .await
        .map_err(describe_updater_error)?;

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

fn build_tauri_updater(
    app: &tauri::AppHandle,
    endpoint: Option<String>,
    public_key: Option<String>,
) -> Result<Updater, String> {
    let mut builder = app.updater_builder();

    if let Some(endpoint) = normalize_optional_text(endpoint) {
        let url = Url::parse(&endpoint).map_err(|error| format!("更新地址无效: {error}"))?;
        match url.scheme() {
            "https" | "http" => {}
            scheme => {
                return Err(format!(
                    "更新地址只允许 http/https，当前 scheme 为 {scheme}。"
                ));
            }
        }
        builder = builder
            .endpoints(vec![url])
            .map_err(describe_updater_error)?;
    }

    if let Some(public_key) = normalize_optional_text(public_key) {
        builder = builder.pubkey(public_key);
    }

    let app_for_exit = app.clone();
    builder = builder.on_before_exit(move || {
        sidecar::run_shutdown_maintenance(&app_for_exit);
        sidecar::stop_sidecar(&app_for_exit);
    });

    builder.build().map_err(describe_updater_error)
}

fn normalize_optional_text(value: Option<String>) -> Option<String> {
    value
        .map(|item| item.trim().to_owned())
        .filter(|item| !item.is_empty())
}

fn describe_updater_error(error: tauri_plugin_updater::Error) -> String {
    let message = error.to_string();
    if message.contains("empty endpoints") || message.contains("Updater endpoints are empty") {
        return "尚未配置更新地址。请填写软件更新源地址，或在打包配置中内置更新地址。".to_owned();
    }

    if message.contains("signature") || message.contains("pubkey") || message.contains("public key")
    {
        return format!("更新签名校验失败或尚未配置签名公钥: {message}");
    }

    format!("软件更新执行失败: {message}")
}
