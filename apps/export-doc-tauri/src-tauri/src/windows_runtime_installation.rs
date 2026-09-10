use std::{
    process::{Child, Command, ExitStatus, Stdio},
    time::{Duration, Instant},
};

use windows_sys::Win32::{
    Foundation::{CloseHandle, GetLastError, ERROR_ALREADY_EXISTS, HANDLE},
    System::Threading::CreateMutexW,
};

const INSTALLER_TIMEOUT: Duration = Duration::from_secs(900);

pub(crate) struct InstallationGuard(HANDLE);

impl InstallationGuard {
    pub(crate) fn acquire() -> Result<Option<Self>, String> {
        Self::acquire_named("Local\\ExportDocManager.RuntimeSetup")
    }

    fn acquire_named(name: &str) -> Result<Option<Self>, String> {
        let name: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
        // Keeping the named object open prevents concurrent setup across editions.
        // Windows closes the handle on process exit, including a crash.
        let handle = unsafe { CreateMutexW(std::ptr::null(), 0, name.as_ptr()) };
        if handle.is_null() {
            return Err(format!(
                "无法建立运行组件安装锁：{}",
                std::io::Error::last_os_error()
            ));
        }
        let already_exists = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        let guard = Self(handle);
        Ok((!already_exists).then_some(guard))
    }
}

impl Drop for InstallationGuard {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.0) };
    }
}

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum InstallOutcome {
    Completed,
    Cancelled,
    RestartRequired,
}

pub(crate) fn install_runtime(installer: &std::path::Path) -> Result<InstallOutcome, String> {
    let mut child = Command::new(installer)
        .arg("/install")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|error| {
            format!("无法启动微软 WebView2 安装器：{error}。请检查文件是否被安全软件阻止。")
        })?;
    let status = wait_for_installer(&mut child, INSTALLER_TIMEOUT)?;
    classify_installer_exit_code(status.code())
}

fn classify_installer_exit_code(code: Option<i32>) -> Result<InstallOutcome, String> {
    match code {
        Some(0) => Ok(InstallOutcome::Completed),
        Some(1223 | 1602) => Ok(InstallOutcome::Cancelled),
        Some(1641 | 3010) => Ok(InstallOutcome::RestartRequired),
        Some(1618) => Err("Windows 正在执行另一项安装。请等待其完成，再双击本程序继续安装 WebView2。".to_owned()),
        Some(code) => Err(format!(
            "WebView2 Runtime 安装失败（错误码 {code} / 0x{:08X}）。请重新运行本程序重试；若仍失败，请将诊断日志交给管理员。",
            code as u32
        )),
        None => Err("WebView2 Runtime 安装器被异常终止，未返回退出码。请重新运行本程序重试。".to_owned()),
    }
}

fn wait_for_installer(child: &mut Child, timeout: Duration) -> Result<ExitStatus, String> {
    let deadline = Instant::now() + timeout;
    loop {
        let result = child.try_wait();
        match result {
            Ok(Some(status)) => return Ok(status),
            Ok(None) if Instant::now() < deadline => {
                std::thread::sleep(Duration::from_millis(250));
            }
            _ => {
                crate::sidecar_process::terminate_child_tree(child);
                return Err(match result {
                    Err(error) => format!("无法读取 WebView2 Runtime 安装器状态：{error}。本次安装已停止，请重新运行本程序。"),
                    _ => "WebView2 Runtime 安装等待超时，本次安装已停止。请检查 Windows 是否有待处理的安装或权限提示，再重新运行本程序。".to_owned(),
                });
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn setup_lock_excludes_duplicates_and_is_released_on_drop() {
        let name = format!(
            "Local\\ExportDocManager.WebView2Setup.Test.{}",
            std::process::id()
        );
        let first = InstallationGuard::acquire_named(&name).unwrap().unwrap();
        assert!(InstallationGuard::acquire_named(&name).unwrap().is_none());
        drop(first);
        assert!(InstallationGuard::acquire_named(&name).unwrap().is_some());
    }

    #[test]
    fn installer_exit_codes_distinguish_cancel_restart_and_failure() {
        assert_eq!(
            classify_installer_exit_code(Some(0)).unwrap(),
            InstallOutcome::Completed
        );
        for code in [1223, 1602] {
            assert_eq!(
                classify_installer_exit_code(Some(code)).unwrap(),
                InstallOutcome::Cancelled
            );
        }
        for code in [1641, 3010] {
            assert_eq!(
                classify_installer_exit_code(Some(code)).unwrap(),
                InstallOutcome::RestartRequired
            );
        }
        assert!(classify_installer_exit_code(Some(1618))
            .unwrap_err()
            .contains("另一项安装"));
        assert!(classify_installer_exit_code(Some(1603))
            .unwrap_err()
            .contains("0x00000643"));
        assert!(classify_installer_exit_code(None).is_err());
    }

    #[test]
    fn timed_out_installer_is_stopped_before_returning() {
        use std::os::windows::process::CommandExt;
        use windows_sys::Win32::System::Threading::CREATE_NO_WINDOW;

        let executable = std::path::PathBuf::from(std::env::var_os("SystemRoot").unwrap())
            .join("System32")
            .join("ping.exe");
        let mut child = Command::new(executable)
            .args(["-n", "60", "127.0.0.1"])
            .creation_flags(CREATE_NO_WINDOW)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .unwrap();
        assert!(wait_for_installer(&mut child, Duration::ZERO)
            .unwrap_err()
            .contains("超时"));
        assert!(child.try_wait().unwrap().is_some());
    }
}
