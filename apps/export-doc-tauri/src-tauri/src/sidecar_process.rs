use std::{
    error::Error,
    io::{Read, Write},
    process::{Child, Command, Stdio},
    thread,
    time::{Duration, Instant},
};

use crate::runtime_paths::RuntimePaths;

const GRACEFUL_PROCESS_EXIT_TIMEOUT: Duration = Duration::from_secs(15);
const FORCED_PROCESS_EXIT_TIMEOUT: Duration = Duration::from_secs(3);

pub(crate) fn write_sidecar_launch_marker<W: Write>(
    log: &mut W,
    listen_url: &str,
    paths: &RuntimePaths,
) -> Result<(), Box<dyn Error>> {
    writeln!(
        log,
        "\n=== Starting API sidecar at {listen_url}; app_root='{}'; data_root='{}' ===",
        paths.app_root.display(),
        paths.data_root.display()
    )
    .map_err(|error| format!("Failed to write sidecar launch marker: {error}").into())
}

pub(crate) fn spawn_log_pump<R>(
    reader: R,
    writer: crate::log_rotation::RotatingLogWriter,
    stream_name: &'static str,
) where
    R: Read + Send + 'static,
{
    thread::spawn(move || {
        if let Err(error) = crate::log_rotation::copy_to_rotating_log(reader, writer) {
            eprintln!("API sidecar {stream_name} log pump failed: {error}");
        }
    });
}

pub(crate) fn stop_process(child: &mut Child) {
    if !wait_for_child_exit(child, GRACEFUL_PROCESS_EXIT_TIMEOUT) {
        terminate_child_tree(child);
    }
}

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

#[cfg(unix)]
pub(crate) fn terminate_child_tree(child: &mut Child) {
    unsafe extern "C" {
        fn kill(pid: i32, signal: i32) -> i32;
    }
    const SIGTERM: i32 = 15;
    const SIGKILL: i32 = 9;

    if child.try_wait().ok().flatten().is_some() {
        return;
    }
    let Ok(process_group_id) = i32::try_from(child.id()) else {
        let _ = child.kill();
        let _ = child.wait();
        return;
    };
    unsafe {
        let _ = kill(-process_group_id, SIGTERM);
    }
    if wait_for_child_exit(child, FORCED_PROCESS_EXIT_TIMEOUT) {
        return;
    }
    unsafe {
        let _ = kill(-process_group_id, SIGKILL);
    }
    if !wait_for_child_exit(child, FORCED_PROCESS_EXIT_TIMEOUT) {
        let _ = child.kill();
        let _ = child.wait();
    }
}

#[cfg(not(any(windows, unix)))]
pub(crate) fn terminate_child_tree(child: &mut Child) {
    let _ = child.kill();
    let _ = child.wait();
}
