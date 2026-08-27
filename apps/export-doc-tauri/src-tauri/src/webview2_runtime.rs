#[cfg(windows)]
use std::{
    fs,
    io::Read,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    time::{Duration, Instant},
};

#[cfg(windows)]
use serde::Deserialize;
#[cfg(windows)]
use sha2::{Digest, Sha256};

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum StartupDecision {
    Continue,
    Exit,
}

#[cfg(windows)]
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeInstallerManifest {
    schema_version: u32,
    file_name: String,
    architecture: String,
    sha256: String,
    bytes: u64,
}

#[cfg(windows)]
const INSTALLER_TIMEOUT: Duration = Duration::from_secs(900);
#[cfg(windows)]
const MINIMUM_INSTALLER_BYTES: u64 = 50 * 1024 * 1024;

#[cfg(windows)]
#[derive(Debug, PartialEq, Eq)]
enum InstallOutcome {
    Completed,
    RestartRequired,
}

pub(crate) fn ensure_available() -> Result<StartupDecision, String> {
    #[cfg(not(windows))]
    {
        return Ok(StartupDecision::Continue);
    }

    #[cfg(windows)]
    {
        if let Some(error) = unsupported_windows_error()? {
            show_startup_error(&error);
            return Ok(StartupDecision::Exit);
        }

        if detect_runtime().is_some() {
            return Ok(StartupDecision::Continue);
        }

        let executable_directory = std::env::current_exe()
            .ok()
            .and_then(|path| path.parent().map(Path::to_path_buf))
            .ok_or_else(|| "无法确定程序目录，无法检查 WebView2 Runtime。".to_owned())?;
        let installer = find_installer(&executable_directory);
        let Some(installer) = installer else {
            show_startup_error(
                "当前电脑没有可用的 Microsoft Edge WebView2 Runtime。\n\n请安装微软官方 WebView2 Runtime 后重新运行本程序。\n\n下载地址：\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
            );
            return Ok(StartupDecision::Exit);
        };

        verify_installer(&installer)?;
        let result = rfd::MessageDialog::new()
            .set_level(rfd::MessageLevel::Warning)
            .set_title("需要安装 WebView2 Runtime")
            .set_description(
                "当前电脑没有可用的 Microsoft Edge WebView2 Runtime。\n\n程序将使用随包提供的微软官方离线安装器。安装过程中可能显示 Windows 管理员权限确认，安装完成后程序会继续启动。",
            )
            .set_buttons(rfd::MessageButtons::OkCancel)
            .show();

        if result != rfd::MessageDialogResult::Ok {
            return Ok(StartupDecision::Exit);
        }

        if install_runtime(&installer)? == InstallOutcome::RestartRequired {
            show_restart_required();
            return Ok(StartupDecision::Exit);
        }
        if wait_for_runtime(Duration::from_secs(15)).is_some() {
            return Ok(StartupDecision::Continue);
        }

        show_startup_error(
            "WebView2 Runtime 安装器已运行，但当前进程仍无法发现运行时。请注销或重启 Windows 后再运行本程序。",
        );
        Ok(StartupDecision::Exit)
    }
}

#[cfg(windows)]
fn unsupported_windows_error() -> Result<Option<String>, String> {
    let build = read_windows_build()?;
    Ok((!windows_build_is_supported(build)).then(|| {
        format!(
            "当前 Windows 内部版本为 {build}。本程序需要 Windows 10 1809（内部版本 17763）或更高版本，当前系统不受支持。"
        )
    }))
}

#[cfg(windows)]
fn windows_build_is_supported(build: u32) -> bool {
    build >= 17_763
}

#[cfg(windows)]
fn read_windows_build() -> Result<u32, String> {
    use std::ffi::c_void;
    use windows_sys::Win32::System::Registry::{
        RegGetValueW, HKEY_LOCAL_MACHINE, REG_SZ, RRF_RT_REG_SZ, RRF_SUBKEY_WOW6464KEY,
    };

    let subkey: Vec<u16> = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();
    let value: Vec<u16> = "CurrentBuildNumber"
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();
    let mut buffer = [0u16; 32];
    let mut bytes = (buffer.len() * std::mem::size_of::<u16>()) as u32;
    let mut value_type = 0u32;
    let result = unsafe {
        RegGetValueW(
            HKEY_LOCAL_MACHINE,
            subkey.as_ptr(),
            value.as_ptr(),
            RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
            &mut value_type,
            buffer.as_mut_ptr().cast::<c_void>(),
            &mut bytes,
        )
    };
    if result != 0 {
        return Err(format!(
            "无法读取 Windows 内部版本：{}",
            std::io::Error::from_raw_os_error(result as i32)
        ));
    }
    if value_type != REG_SZ {
        return Err("Windows 内部版本注册表值类型无效。".to_owned());
    }

    let length = (bytes as usize / std::mem::size_of::<u16>()).min(buffer.len());
    String::from_utf16(&buffer[..length])
        .map_err(|error| format!("Windows 内部版本不是有效文本：{error}"))?
        .trim_end_matches('\0')
        .parse()
        .map_err(|error| format!("Windows 内部版本不是有效数字：{error}"))
}

#[cfg(windows)]
fn detect_runtime() -> Option<String> {
    tauri::webview_version()
        .ok()
        .filter(|version| !version.trim().is_empty())
}

#[cfg(windows)]
fn wait_for_runtime(timeout: Duration) -> Option<String> {
    let deadline = Instant::now() + timeout;
    loop {
        if let Some(version) = detect_runtime() {
            return Some(version);
        }
        if Instant::now() >= deadline {
            return None;
        }
        std::thread::sleep(Duration::from_millis(500));
    }
}

#[cfg(windows)]
fn installer_file_name() -> &'static str {
    if cfg!(target_arch = "aarch64") {
        "MicrosoftEdgeWebView2RuntimeInstallerARM64.exe"
    } else {
        "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
    }
}

#[cfg(windows)]
fn installer_architecture() -> &'static str {
    if cfg!(target_arch = "aarch64") {
        "arm64"
    } else {
        "x64"
    }
}

#[cfg(windows)]
fn find_installer(executable_directory: &Path) -> Option<PathBuf> {
    let installer = executable_directory
        .join("WebView2Runtime")
        .join(installer_file_name());
    installer.is_file().then_some(installer)
}

#[cfg(windows)]
fn verify_installer(installer: &Path) -> Result<(), String> {
    let expected_name = installer_file_name();
    let actual_name = installer
        .file_name()
        .and_then(|value| value.to_str())
        .unwrap_or_default();
    if actual_name != expected_name {
        return Err(format!(
            "WebView2 Runtime 安装器文件名不匹配：期望 {expected_name}，实际 {actual_name}。"
        ));
    }

    let metadata = fs::metadata(installer)
        .map_err(|error| format!("无法读取 WebView2 Runtime 安装器：{error}"))?;
    if metadata.len() < MINIMUM_INSTALLER_BYTES {
        return Err("WebView2 Runtime 安装器体积异常，可能不是离线安装包。".to_owned());
    }

    let manifest_path = installer
        .parent()
        .map(|parent| parent.join("webview2-runtime.json"))
        .ok_or_else(|| "WebView2 Runtime 安装器路径无效。".to_owned())?;
    if !manifest_path.is_file() {
        return Err("WebView2 Runtime 安装器缺少校验清单。".to_owned());
    }

    let manifest: RuntimeInstallerManifest = serde_json::from_slice(
        &fs::read(&manifest_path)
            .map_err(|error| format!("无法读取 WebView2 Runtime 校验清单：{error}"))?,
    )
    .map_err(|error| format!("WebView2 Runtime 校验清单格式无效：{error}"))?;
    if manifest.schema_version != 1
        || manifest.file_name != expected_name
        || manifest.architecture != installer_architecture()
        || manifest.bytes != metadata.len()
    {
        return Err("WebView2 Runtime 安装器与校验清单不匹配。".to_owned());
    }

    let mut file = fs::File::open(installer)
        .map_err(|error| format!("无法读取 WebView2 Runtime 安装器：{error}"))?;
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 1024 * 1024];
    loop {
        let count = file
            .read(&mut buffer)
            .map_err(|error| format!("无法校验 WebView2 Runtime 安装器：{error}"))?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }
    let actual_hash = format!("{:x}", hasher.finalize());
    if !actual_hash.eq_ignore_ascii_case(&manifest.sha256) {
        return Err("WebView2 Runtime 安装器 SHA-256 校验失败。".to_owned());
    }

    Ok(())
}

#[cfg(windows)]
fn install_runtime(installer: &Path) -> Result<InstallOutcome, String> {
    let mut command = Command::new(installer);
    command
        .arg("/install")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());

    let mut child = command
        .spawn()
        .map_err(|error| format!("无法启动 WebView2 Runtime 安装器：{error}"))?;
    let status = wait_for_installer(&mut child)?;
    classify_installer_exit_code(status.code())
}

#[cfg(windows)]
fn classify_installer_exit_code(code: Option<i32>) -> Result<InstallOutcome, String> {
    match code {
        Some(0) => Ok(InstallOutcome::Completed),
        Some(1641 | 3010) => Ok(InstallOutcome::RestartRequired),
        Some(code) => Err(format!("WebView2 Runtime 安装器退出码为 {code}。")),
        None => Err("WebView2 Runtime 安装器被异常终止，未返回退出码。".to_owned()),
    }
}

#[cfg(windows)]
fn wait_for_installer(child: &mut Child) -> Result<std::process::ExitStatus, String> {
    let deadline = Instant::now() + INSTALLER_TIMEOUT;
    loop {
        match child
            .try_wait()
            .map_err(|error| format!("无法读取 WebView2 Runtime 安装器状态：{error}"))?
        {
            Some(status) => return Ok(status),
            None if Instant::now() >= deadline => {
                return Err(
                    "WebView2 Runtime 安装超过 15 分钟仍未结束。安装程序可能仍在后台运行，请等待安装完成后重新启动本程序。"
                        .to_owned(),
                );
            }
            None => std::thread::sleep(Duration::from_millis(250)),
        }
    }
}

#[cfg(windows)]
fn show_startup_error(message: &str) {
    let _ = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Error)
        .set_title("无法启动程序")
        .set_description(message)
        .set_buttons(rfd::MessageButtons::Ok)
        .show();
}

#[cfg(windows)]
fn show_restart_required() {
    let _ = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Info)
        .set_title("需要重启 Windows")
        .set_description(
            "WebView2 Runtime 已成功安装，但 Windows 要求重启后才能使用。请重启电脑，再运行本程序。",
        )
        .set_buttons(rfd::MessageButtons::Ok)
        .show();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn startup_decision_is_explicit() {
        assert_ne!(StartupDecision::Continue, StartupDecision::Exit);
    }

    #[cfg(windows)]
    #[test]
    fn installer_name_matches_target_architecture() {
        let name = installer_file_name();
        if cfg!(target_arch = "aarch64") {
            assert_eq!(name, "MicrosoftEdgeWebView2RuntimeInstallerARM64.exe");
            assert_eq!(installer_architecture(), "arm64");
        } else {
            assert_eq!(name, "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
            assert_eq!(installer_architecture(), "x64");
        }
    }

    #[cfg(windows)]
    #[test]
    fn windows_10_ltsc_2019_is_the_minimum_supported_build() {
        assert!(!windows_build_is_supported(17_762));
        assert!(windows_build_is_supported(17_763));
        assert!(windows_build_is_supported(19_044));
        assert!(windows_build_is_supported(22_000));
    }

    #[cfg(windows)]
    #[test]
    fn installer_exit_codes_distinguish_restart_from_failure() {
        assert_eq!(
            classify_installer_exit_code(Some(0)).unwrap(),
            InstallOutcome::Completed
        );
        assert_eq!(
            classify_installer_exit_code(Some(1641)).unwrap(),
            InstallOutcome::RestartRequired
        );
        assert_eq!(
            classify_installer_exit_code(Some(3010)).unwrap(),
            InstallOutcome::RestartRequired
        );
        assert!(classify_installer_exit_code(Some(1603)).is_err());
        assert!(classify_installer_exit_code(None).is_err());
    }
}
