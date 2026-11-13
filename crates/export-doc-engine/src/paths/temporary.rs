//! Anonymous managed scratch files: the OS releases bytes when the last handle closes.
use super::*;

pub fn anonymous_file(directory: &Path) -> Result<fs::File, String> {
    ensure_safe_absolute(directory)?;
    crate::secrets::private_directory(directory)?;
    let path = directory.join(format!("{}.tmp", nonce()?));
    let mut options = fs::OpenOptions::new();
    options.create_new(true).read(true).write(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        options.custom_flags(0x04000000); // FILE_FLAG_DELETE_ON_CLOSE
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let file = options.open(&path).map_err(|e| e.to_string())?;
    #[cfg(unix)]
    fs::remove_file(path).map_err(|e| e.to_string())?;
    Ok(file)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn scratch_is_readable_and_removed_after_last_handle_closes() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let service = root.service();
        let directory = service.paths.cache_root.join("AnonymousTest");
        let mut file = anonymous_file(&directory).unwrap();
        file.write_all(b"scratch").unwrap();
        let duplicate = file.try_clone().unwrap();
        drop(file);
        assert_eq!(duplicate.metadata().unwrap().len(), 7);
        drop(duplicate);
        assert_eq!(fs::read_dir(directory).unwrap().count(), 0);
        service.close().unwrap();
    }
}
