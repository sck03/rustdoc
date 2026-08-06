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
    rotate_if_needed(path);
    OpenOptions::new().create(true).append(true).open(path)
}

fn rotate_if_needed(path: &Path) {
    let oversized = fs::metadata(path)
        .map(|metadata| metadata.len() > DEFAULT_LOG_FILE_SIZE_BYTES)
        .unwrap_or(false);
    if !oversized {
        return;
    }

    for index in (1..=ROTATED_LOG_COUNT).rev() {
        let source = if index == 1 {
            path.to_path_buf()
        } else {
            rotated_path(path, index - 1)
        };
        let target = rotated_path(path, index);
        let _ = fs::remove_file(&target);
        let _ = fs::rename(&source, &target);
    }
}

fn rotated_path(path: &Path, index: usize) -> PathBuf {
    let mut rotated = path.as_os_str().to_os_string();
    rotated.push(format!(".{index}"));
    PathBuf::from(rotated)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{io::Write as _, time::SystemTime};

    #[test]
    fn rotates_oversized_log_before_opening_new_append_handle() {
        let unique = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let root = std::env::temp_dir().join(format!(
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
}
