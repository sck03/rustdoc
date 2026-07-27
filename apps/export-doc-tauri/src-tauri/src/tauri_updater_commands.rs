use serde::Serialize;
use tauri_plugin_updater::{Updater, UpdaterExt};

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
) -> Result<TauriUpdaterCheckResult, String> {
    let updater = build_tauri_updater(&app)?;
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
) -> Result<TauriUpdaterInstallResult, String> {
    let updater = build_tauri_updater(&app)?;
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

fn build_tauri_updater(app: &tauri::AppHandle) -> Result<Updater, String> {
    let mut builder = app.updater_builder();

    let app_for_exit = app.clone();
    builder = builder.on_before_exit(move || {
        sidecar::run_shutdown_maintenance(&app_for_exit);
        sidecar::stop_sidecar(&app_for_exit);
    });

    builder.build().map_err(describe_updater_error)
}

fn describe_updater_error(error: tauri_plugin_updater::Error) -> String {
    let message = error.to_string();
    if message.contains("empty endpoints") || message.contains("Updater endpoints are empty") {
        return "当前安装包尚未配置正式更新源。请使用发布流程生成的签名安装包，或联系软件维护人员。".to_owned();
    }

    if message.contains("signature") || message.contains("pubkey") || message.contains("public key")
    {
        return format!("更新签名校验失败或安装包未内置签名公钥: {message}");
    }

    format!("软件更新执行失败: {message}")
}
