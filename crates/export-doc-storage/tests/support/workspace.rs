use std::{
    fs, io,
    path::{Path, PathBuf},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub fn create(namespace: &str) -> PathBuf {
    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_nanos();
    let root = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../.codex-runtime")
        .join(namespace)
        .join(format!("{}-{stamp}", std::process::id()));
    fs::create_dir_all(&root).unwrap();
    root
}

/// Call after database handles have left scope. Windows scanners can briefly
/// retain a file without delete sharing; persistent or unrelated errors fail.
pub fn remove(root: &Path) -> io::Result<()> {
    let started = Instant::now();
    loop {
        match fs::remove_dir_all(root) {
            Ok(()) => return Ok(()),
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
            Err(error)
                if cfg!(windows)
                    && matches!(error.raw_os_error(), Some(32 | 33))
                    && started.elapsed() < Duration::from_secs(2) =>
            {
                std::thread::sleep(Duration::from_millis(25));
            }
            Err(error) => return Err(error),
        }
    }
}
