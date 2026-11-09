use serde_json::Value;

#[tauri::command]
pub async fn detect_single_window_client_directories() -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(detect)
        .await
        .map_err(|_| "客户端目录检测未完成，请手动选择目录。".to_owned())?
}

#[cfg(not(windows))]
fn detect() -> Result<Value, String> {
    Err("自动检测仅支持 Windows 单一窗口导入客户端，请手动选择目录。".into())
}

#[cfg(windows)]
fn detect() -> Result<Value, String> {
    use export_doc_single_window::client_settings::{MAX_SETTINGS_BYTES, directories};
    use std::{fs::File, io::Read, path::PathBuf};
    // This is the official client's own per-user configuration, not our DataRoot.
    let root = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .ok_or("无法定位客户端用户配置，请手动选择目录。")?;
    let path = root.join("SIC/UserSetting/DFWJobSettings.xml");
    export_doc_engine::paths::ensure_safe_absolute(&path)?;
    let file = File::open(&path).map_err(|e| {
        if e.kind() == std::io::ErrorKind::NotFound {
            "未找到客户端目录配置，请先在导入客户端保存任务目录，或手动选择。".to_owned()
        } else {
            "无法读取客户端目录配置，请检查访问权限或手动选择。".to_owned()
        }
    })?;
    let mut bytes = Vec::new();
    file.take(MAX_SETTINGS_BYTES as u64 + 1)
        .read_to_end(&mut bytes)
        .map_err(|_| "读取客户端目录配置失败，请手动选择。".to_owned())?;
    directories(&bytes)
}
