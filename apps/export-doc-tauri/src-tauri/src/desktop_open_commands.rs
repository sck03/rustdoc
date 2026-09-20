use std::{
    ffi::OsString,
    fs, io,
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
};

#[tauri::command]
pub(crate) fn open_path(path: String) -> Result<(), String> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return Err("路径不能为空。".to_owned());
    }

    let input = PathBuf::from(trimmed);
    let (target, is_file) = resolve_open_path_target(&input)?;

    open_existing_path(&target, is_file)
}

pub(crate) fn resolve_open_path_target(input: &Path) -> Result<(PathBuf, bool), String> {
    match fs::metadata(input) {
        Ok(metadata) => Ok((input.to_path_buf(), metadata.is_file())),
        Err(error) if error.kind() == io::ErrorKind::NotFound && input.extension().is_some() => {
            if let Some(parent) = input.parent() {
                if !parent.as_os_str().is_empty() {
                    if let Ok(parent_metadata) = fs::metadata(parent) {
                        if parent_metadata.is_dir() {
                            return Ok((parent.to_path_buf(), false));
                        }
                    }
                }
            }

            Err(format!("无法打开路径 '{}': {error}", input.display()))
        }
        Err(error) => Err(format!("无法打开路径 '{}': {error}", input.display())),
    }
}

#[cfg(windows)]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[cfg(target_os = "macos")]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[cfg(all(unix, not(target_os = "macos")))]
fn open_existing_path(path: &Path, reveal_file: bool) -> Result<(), String> {
    spawn_open_command(build_open_command(path, reveal_file))
}

#[derive(Debug, PartialEq, Eq)]
pub(crate) struct OpenCommandSpec {
    pub(crate) program: &'static str,
    pub(crate) program_name: &'static str,
    pub(crate) args: Vec<OsString>,
}

#[cfg(windows)]
pub(crate) fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let args = if reveal_file {
        vec![OsString::from(format!("/select,{}", path.display()))]
    } else {
        vec![path.as_os_str().to_owned()]
    };

    OpenCommandSpec {
        program: "explorer",
        program_name: "Windows Explorer",
        args,
    }
}

#[cfg(target_os = "macos")]
pub(crate) fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let mut args = Vec::new();
    if reveal_file {
        args.push(OsString::from("-R"));
    }

    args.push(path.as_os_str().to_owned());

    OpenCommandSpec {
        program: "open",
        program_name: "open",
        args,
    }
}

#[cfg(all(unix, not(target_os = "macos")))]
pub(crate) fn build_open_command(path: &Path, reveal_file: bool) -> OpenCommandSpec {
    let target = if reveal_file {
        path.parent().unwrap_or(path)
    } else {
        path
    };

    OpenCommandSpec {
        program: "xdg-open",
        program_name: "xdg-open",
        args: vec![target.as_os_str().to_owned()],
    }
}

fn spawn_open_command(spec: OpenCommandSpec) -> Result<(), String> {
    let mut command = Command::new(spec.program);
    command.args(spec.args);
    spawn_detached(command, spec.program_name)
}

fn spawn_detached(mut command: Command, program_name: &str) -> Result<(), String> {
    command
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        command.creation_flags(CREATE_NO_WINDOW);
    }

    let child = command
        .spawn()
        .map_err(|error| format!("无法启动 {program_name}: {error}"))?;
    reap_detached_child(child);
    Ok(())
}

#[cfg(unix)]
fn reap_detached_child(mut child: Child) {
    std::thread::spawn(move || {
        let _ = child.wait();
    });
}

#[cfg(not(unix))]
fn reap_detached_child(_child: Child) {}
