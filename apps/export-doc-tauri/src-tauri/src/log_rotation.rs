use std::{
    fs::{self, File, OpenOptions},
    io,
    path::{Path, PathBuf},
};

pub(crate) const DEFAULT_LOG_FILE_SIZE_BYTES: u64 = 20 * 1024 * 1024;
const ROTATED_LOG_COUNT: usize = 3;

pub(crate) fn open_append_log_file(path: &Path) -> io::Result<File> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    rotate_if_needed(path)?;
    OpenOptions::new().create(true).append(true).open(path)
}

fn rotate_if_needed(path: &Path) -> io::Result<()> {
    match fs::metadata(path) {
        Ok(metadata) if metadata.len() < DEFAULT_LOG_FILE_SIZE_BYTES => return Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
        _ => {}
    }
    match fs::remove_file(rotated_path(path, ROTATED_LOG_COUNT)) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(error),
    }
    for index in (1..=ROTATED_LOG_COUNT).rev() {
        let source = if index == 1 {
            path.to_path_buf()
        } else {
            rotated_path(path, index - 1)
        };
        match fs::rename(source, rotated_path(path, index)) {
            Ok(()) => {}
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(error),
        }
    }
    Ok(())
}

fn rotated_path(path: &Path, index: usize) -> PathBuf {
    let mut rotated = path.as_os_str().to_os_string();
    rotated.push(format!(".{index}"));
    PathBuf::from(rotated)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{io::Write, time::SystemTime};

    #[test]
    fn rotates_oversized_log_before_opening_new_append_handle() {
        let unique = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../../.codex-runtime/log-rotation-tests")
            .join(format!(
                "exportdoc-log-rotation-{}-{unique}",
                std::process::id()
            ));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("desktop.log");
        let previous_path = rotated_path(&path, 1);
        fs::write(&previous_path, b"previous").unwrap();
        let oversized = File::create(&path).unwrap();
        oversized.set_len(DEFAULT_LOG_FILE_SIZE_BYTES + 1).unwrap();
        drop(oversized);

        let mut active = open_append_log_file(&path).unwrap();
        active.write_all(b"next").unwrap();
        active.flush().unwrap();
        drop(active);

        assert_eq!(fs::read(&path).unwrap(), b"next");
        assert_eq!(
            fs::metadata(rotated_path(&path, 1)).unwrap().len(),
            DEFAULT_LOG_FILE_SIZE_BYTES + 1
        );
        assert_eq!(fs::read(rotated_path(&path, 2)).unwrap(), b"previous");

        fs::remove_dir_all(root).unwrap();
    }
    #[test]
    fn failed_rotation_preserves_the_active_log_and_reports_the_error() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../../.codex-runtime/log-rotation-tests")
            .join(format!(
                "failure-{}",
                SystemTime::now()
                    .duration_since(SystemTime::UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("desktop.log");
        File::create(&path)
            .unwrap()
            .set_len(DEFAULT_LOG_FILE_SIZE_BYTES)
            .unwrap();
        fs::create_dir(rotated_path(&path, ROTATED_LOG_COUNT)).unwrap();
        assert!(open_append_log_file(&path).is_err());
        assert_eq!(
            fs::metadata(&path).unwrap().len(),
            DEFAULT_LOG_FILE_SIZE_BYTES
        );
        fs::remove_dir_all(root).unwrap();
    }
}
