use std::{
    fs,
    io::Read,
    path::Path,
    time::{Duration, Instant},
};

use serde::Deserialize;
use sha2::{Digest, Sha256};

use crate::windows_runtime_installation::{install_runtime, InstallOutcome, InstallationGuard};

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum StartupDecision {
    Continue,
    Exit,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeInstallerManifest {
    schema_version: u32,
    file_name: String,
    architecture: String,
    sha256: String,
    bytes: u64,
}

pub(crate) fn ensure_available() -> Result<StartupDecision, String> {
    crate::windows_runtime_probe::check_windows_version()?;
    if runtime_available() {
        return Ok(StartupDecision::Continue);
    }
    let Some(_guard) = InstallationGuard::acquire()? else {
        show_information(
            "运行组件正在安装",
            "另一个程序窗口正在准备 WebView2。请完成该窗口中的安装，安装完成后会自动启动程序。",
        );
        return Ok(StartupDecision::Exit);
    };
    // Another launch may have completed installation while this launch acquired the guard.
    if runtime_available() {
        return Ok(StartupDecision::Continue);
    }
    let executable =
        std::env::current_exe().map_err(|error| format!("无法确定程序目录：{error}"))?;
    let root = executable.parent().ok_or("无法确定程序目录。")?;
    let installer = root.join("WebView2Runtime").join(installer_file_name());
    if !installer.is_file() {
        return Err("当前电脑缺少 Microsoft Edge WebView2 Runtime，程序包中也没有离线安装器。请重新运行本产品安装包，或从微软官网下载 WebView2 后再次启动。\nhttps://developer.microsoft.com/microsoft-edge/webview2/".into());
    }
    verify_installer(&installer)?;
    let install_label = "安装并启动".to_owned();
    let result = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Info)
        .set_title("首次运行准备")
        .set_description("还需要安装 Microsoft Edge WebView2 才能显示程序界面。程序已附带微软官方离线安装器，无需另外下载。\n\n点击“安装并启动”后可查看安装进度；如 Windows 请求权限，请按系统提示操作。安装完成后自动继续启动。")
        .set_buttons(rfd::MessageButtons::OkCancelCustom(install_label.clone(), "暂不安装".into()))
        .show();
    if result != rfd::MessageDialogResult::Custom(install_label) {
        return Ok(StartupDecision::Exit);
    }
    match install_runtime(&installer)? {
        InstallOutcome::Cancelled => return Ok(StartupDecision::Exit),
        InstallOutcome::RestartRequired => {
            show_information(
                "需要重启 Windows",
                "WebView2 已安装。Windows 要求重启后才能使用，请重启电脑，再运行本程序。",
            );
            return Ok(StartupDecision::Exit);
        }
        InstallOutcome::Completed => {}
    }
    let deadline = Instant::now() + Duration::from_secs(15);
    loop {
        if runtime_available() {
            return Ok(StartupDecision::Continue);
        }
        if Instant::now() >= deadline {
            return Err("WebView2 安装器已结束，但仍未检测到可用运行组件。请重启 Windows 后再运行本程序；若仍失败，请重新运行安装包修复。".into());
        }
        std::thread::sleep(Duration::from_millis(500));
    }
}

fn runtime_available() -> bool {
    tauri::webview_version().is_ok_and(|version| !version.trim().is_empty())
}

fn installer_file_name() -> &'static str {
    if cfg!(target_arch = "aarch64") {
        "MicrosoftEdgeWebView2RuntimeInstallerARM64.exe"
    } else {
        "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
    }
}

fn verify_installer(installer: &Path) -> Result<(), String> {
    let manifest_path = installer
        .parent()
        .ok_or("WebView2 安装器路径无效。")?
        .join("webview2-runtime.json");
    let manifest: RuntimeInstallerManifest = serde_json::from_slice(
        &fs::read(manifest_path).map_err(|error| format!("无法读取 WebView2 校验清单：{error}"))?,
    )
    .map_err(|error| format!("WebView2 校验清单格式无效：{error}"))?;
    let architecture = if cfg!(target_arch = "aarch64") {
        "arm64"
    } else {
        "x64"
    };
    let mut file =
        fs::File::open(installer).map_err(|error| format!("无法读取 WebView2 安装器：{error}"))?;
    let length = file
        .metadata()
        .map_err(|error| format!("无法读取安装器大小：{error}"))?
        .len();
    if manifest.schema_version != 1
        || manifest.file_name != installer_file_name()
        || manifest.architecture != architecture
        || manifest.bytes != length
        || length < 50 * 1024 * 1024
    {
        return Err("WebView2 离线安装器与校验清单不匹配，请重新获取完整程序包。".into());
    }
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 64 * 1024];
    loop {
        let count = file
            .read(&mut buffer)
            .map_err(|error| format!("无法校验 WebView2 安装器：{error}"))?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }
    if !format!("{:x}", hasher.finalize()).eq_ignore_ascii_case(&manifest.sha256) {
        return Err("WebView2 安装器 SHA-256 校验失败，请重新获取完整程序包。".into());
    }
    Ok(())
}

fn show_information(title: &str, message: &str) {
    rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Info)
        .set_title(title)
        .set_description(message)
        .set_buttons(rfd::MessageButtons::Ok)
        .show();
}
