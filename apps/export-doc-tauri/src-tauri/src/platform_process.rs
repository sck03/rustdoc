use std::{
    process::{Child, Command, Stdio},
    thread,
    time::{Duration, Instant},
};
const FORCED_PROCESS_EXIT_TIMEOUT: Duration = Duration::from_secs(3);
fn wait_for_child_exit(child: &mut Child, timeout: Duration) -> bool {
    let deadline = Instant::now() + timeout;
    loop {
        match child.try_wait() {
            Ok(Some(_)) => return true,
            Ok(None) if Instant::now() < deadline => thread::sleep(Duration::from_millis(50)),
            Ok(None) | Err(_) => return false,
        }
    }
}

#[cfg(windows)]
pub(crate) fn terminate_child_tree(child: &mut Child) {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x08000000;

    if child.try_wait().ok().flatten().is_none() {
        let _ = Command::new("taskkill")
            .args(["/PID", &child.id().to_string(), "/T", "/F"])
            .creation_flags(CREATE_NO_WINDOW)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status();
    }
    if !wait_for_child_exit(child, FORCED_PROCESS_EXIT_TIMEOUT) {
        let _ = child.kill();
        let _ = child.wait();
    }
}
