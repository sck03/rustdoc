use std::path::PathBuf;
/// Declare this guard after every database/process owner in a fixture.
pub(crate) struct ScratchDirectory(pub PathBuf);
impl Drop for ScratchDirectory {
    fn drop(&mut self) {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .join(".codex-runtime");
        assert!(
            self.0.starts_with(&workspace),
            "test cleanup escaped its runtime root"
        );
        crate::paths::ensure_safe_absolute(&self.0).expect("unsafe test cleanup path");
        if let Err(cause) = std::fs::remove_dir_all(&self.0) {
            if std::thread::panicking() {
                eprintln!(
                    "Test directory cleanup failed: {}: {cause}",
                    self.0.display()
                );
            } else {
                panic!(
                    "Test directory cleanup failed: {}: {cause}",
                    self.0.display()
                );
            }
        }
    }
}
