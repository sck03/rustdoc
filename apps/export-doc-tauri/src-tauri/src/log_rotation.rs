use std::{
    fs::{self, File, OpenOptions},
    io::{self, Read, Write},
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

pub(crate) struct RotatingLogWriter {
    path: PathBuf,
    file: Option<File>,
    current_size: u64,
    maximum_size: u64,
}

impl RotatingLogWriter {
    pub(crate) fn open(path: &Path) -> io::Result<Self> {
        Self::open_with_limit(path, DEFAULT_LOG_FILE_SIZE_BYTES)
    }

    fn open_with_limit(path: &Path, maximum_size: u64) -> io::Result<Self> {
        if maximum_size == 0 {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "rotating log size limit must be greater than zero",
            ));
        }
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        rotate_if_needed_with_limit(path, maximum_size);
        let file = OpenOptions::new().create(true).append(true).open(path)?;
        let current_size = file.metadata()?.len();
        Ok(Self {
            path: path.to_path_buf(),
            file: Some(file),
            current_size,
            maximum_size,
        })
    }

    fn rotate(&mut self) -> io::Result<()> {
        if let Some(mut file) = self.file.take() {
            file.flush()?;
            drop(file);
        }
        rotate_files(&self.path);
        self.file = Some(
            OpenOptions::new()
                .create(true)
                .append(true)
                .open(&self.path)?,
        );
        self.current_size = 0;
        Ok(())
    }
}

impl Write for RotatingLogWriter {
    fn write(&mut self, buffer: &[u8]) -> io::Result<usize> {
        if buffer.is_empty() {
            return Ok(0);
        }
        if self.current_size >= self.maximum_size {
            self.rotate()?;
        }

        let remaining = (self.maximum_size - self.current_size) as usize;
        let write_size = buffer.len().min(remaining);
        let written = self
            .file
            .as_mut()
            .ok_or_else(|| io::Error::other("rotating log file is unavailable"))?
            .write(&buffer[..write_size])?;
        self.current_size += written as u64;
        Ok(written)
    }

    fn flush(&mut self) -> io::Result<()> {
        self.file
            .as_mut()
            .ok_or_else(|| io::Error::other("rotating log file is unavailable"))?
            .flush()
    }
}

pub(crate) fn copy_to_rotating_log<R: Read>(
    mut reader: R,
    mut writer: RotatingLogWriter,
) -> io::Result<()> {
    let mut buffer = [0_u8; 16 * 1024];
    loop {
        let read = reader.read(&mut buffer)?;
        if read == 0 {
            writer.flush()?;
            return Ok(());
        }
        writer.write_all(&buffer[..read])?;
    }
}

fn rotate_if_needed(path: &Path) {
    rotate_if_needed_with_limit(path, DEFAULT_LOG_FILE_SIZE_BYTES);
}

fn rotate_if_needed_with_limit(path: &Path, maximum_size: u64) {
    let oversized = fs::metadata(path)
        .map(|metadata| metadata.len() >= maximum_size)
        .unwrap_or(false);
    if !oversized {
        return;
    }

    rotate_files(path);
}

fn rotate_files(path: &Path) {
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
    use std::time::SystemTime;

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

    #[test]
    fn rotates_while_a_long_running_writer_is_active() {
        let unique = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let root = std::env::temp_dir().join(format!(
            "exportdoc-live-log-rotation-{}-{unique}",
            std::process::id()
        ));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("sidecar.log");

        let mut writer = RotatingLogWriter::open_with_limit(&path, 8).unwrap();
        writer.write_all(b"12345678abcdefgh").unwrap();
        writer.flush().unwrap();
        drop(writer);

        assert_eq!(fs::read(&path).unwrap(), b"abcdefgh");
        assert_eq!(fs::read(rotated_path(&path, 1)).unwrap(), b"12345678");

        fs::remove_dir_all(root).unwrap();
    }
}
