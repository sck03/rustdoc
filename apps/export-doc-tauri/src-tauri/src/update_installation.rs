//! The installer cannot run until the shared backend has stopped successfully.
pub(crate) fn run(
    stop: impl FnOnce() -> Result<(), String>,
    install: impl FnOnce() -> Result<(), String>,
) -> Result<(), String> {
    stop().map_err(|cause| format!("更新前关闭后端失败，未安装更新：{cause}"))?;
    install().map_err(|cause| format!("{cause}；后端已停止，请退出并重启原程序后重试。"))
}
