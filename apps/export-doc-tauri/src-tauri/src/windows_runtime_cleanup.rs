//! Reclaim only the portable bootstrap assets after a working WebView was created.
use std::{fs, io, path::Path};

use export_doc_engine::paths::ensure_safe_absolute;

use super::{InstallationGuard, installer_file_name};
use crate::runtime_paths::RuntimePaths;

pub(crate) fn schedule_after_startup(paths: &RuntimePaths) {
    if !paths.portable {
        return;
    }
    let root = paths.app_root.clone();
    let result = std::thread::Builder::new()
        .name("webview-installer-cleanup".into())
        .spawn(move || {
            let result = (|| {
                // Share the installer guard across editions; never delete an active setup.
                let Some(_guard) = InstallationGuard::acquire()? else {
                    return Ok(0);
                };
                remove_installer_files(&root)
            })();
            match result {
                Ok(0) => {}
                Ok(bytes) => {
                    crate::append_diagnostic_log(
                        "webview2-cleanup.log",
                        "Portable WebView2 installer cleanup",
                        &format!(
                            "Removed {bytes} bytes from {}",
                            root.join("WebView2Runtime").display()
                        ),
                    );
                }
                Err(error) => {
                    crate::write_tauri_error(&format!(
                        "WebView2 installer cleanup deferred until next startup: {error}"
                    ));
                }
            }
        });
    if let Err(error) = result {
        crate::write_tauri_error(&format!(
            "Unable to start WebView2 installer cleanup: {error}"
        ));
    }
}

fn remove_installer_files(app_root: &Path) -> Result<u64, String> {
    ensure_safe_absolute(app_root)?;
    let directory = app_root.join("WebView2Runtime");
    ensure_safe_absolute(&directory)?;
    let entries = match fs::read_dir(&directory) {
        Ok(entries) => entries,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(0),
        Err(error) => return Err(error.to_string()),
    };
    // Preflight the whole flat directory. Unknown files, nested directories and
    // reparse points are preserved. No recursive deletion or manifest-supplied paths.
    let names = [installer_file_name(), "README.md", "webview2-runtime.json"];
    let mut bytes = 0;
    for entry in entries {
        let entry = entry.map_err(|error| error.to_string())?;
        let path = entry.path();
        ensure_safe_absolute(&path)?;
        let metadata = fs::symlink_metadata(&path).map_err(|error| error.to_string())?;
        if !names.iter().any(|name| entry.file_name() == *name) || !metadata.is_file() {
            return Err(format!("Unexpected content preserved: {}", path.display()));
        }
        bytes += metadata.len();
    }
    // Installer first: if it is locked, retain the manifest and notice as well.
    // Missing files are accepted so interrupted cleanup is safe to retry.
    for name in names {
        let path = directory.join(name);
        ensure_safe_absolute(&path)?;
        match fs::remove_file(&path) {
            Ok(()) => {}
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(format!("{}: {error}", path.display())),
        }
    }
    fs::remove_dir(&directory).map_err(|error| format!("{}: {error}", directory.display()))?;
    Ok(bytes)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{os::windows::fs::OpenOptionsExt, path::PathBuf};

    struct Fixture(PathBuf);

    impl Fixture {
        fn new() -> Self {
            let root = std::env::var_os("CARGO_TARGET_DIR")
                .map(PathBuf::from)
                .unwrap_or_else(|| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("target"))
                .join("webview-cleanup-tests")
                .join(format!(
                    "{}-{}",
                    std::process::id(),
                    std::time::SystemTime::now()
                        .duration_since(std::time::UNIX_EPOCH)
                        .unwrap()
                        .as_nanos()
                ));
            fs::create_dir_all(root.join("WebView2Runtime")).unwrap();
            for name in [installer_file_name(), "README.md", "webview2-runtime.json"] {
                fs::write(root.join("WebView2Runtime").join(name), b"fixture").unwrap();
            }
            Self(root)
        }

        fn installer(&self) -> PathBuf {
            self.0.join("WebView2Runtime").join(installer_file_name())
        }
    }

    impl Drop for Fixture {
        fn drop(&mut self) {
            fs::remove_dir_all(&self.0).unwrap();
        }
    }

    #[test]
    fn removes_bootstrap_assets_only_and_allows_repeat_startup() {
        let fixture = Fixture::new();
        fs::create_dir(fixture.0.join("App_Data")).unwrap();
        let data = fixture.0.join("App_Data/exportdoc.db");
        let loader = fixture.0.join("WebView2Loader.dll");
        fs::write(&data, b"business data").unwrap();
        fs::write(&loader, b"loader").unwrap();
        assert_eq!(remove_installer_files(&fixture.0).unwrap(), 21);
        assert!(!fixture.0.join("WebView2Runtime").exists());
        assert_eq!(fs::read(data).unwrap(), b"business data");
        assert_eq!(fs::read(loader).unwrap(), b"loader");
        assert_eq!(remove_installer_files(&fixture.0).unwrap(), 0);
    }

    #[test]
    fn unexpected_files_and_directories_preserve_the_entire_installer() {
        let fixture = Fixture::new();
        let extra = fixture.0.join("WebView2Runtime/customer.txt");
        fs::write(&extra, b"keep").unwrap();
        assert!(remove_installer_files(&fixture.0).is_err());
        assert!(fixture.installer().exists());
        fs::remove_file(&extra).unwrap();
        fs::create_dir(&extra).unwrap();
        assert!(remove_installer_files(&fixture.0).is_err());
        assert!(fixture.installer().exists());
    }

    #[test]
    fn locked_installer_preserves_metadata_and_can_be_retried() {
        let fixture = Fixture::new();
        let locked = fs::OpenOptions::new()
            .read(true)
            .share_mode(0)
            .open(fixture.installer())
            .unwrap();
        assert!(remove_installer_files(&fixture.0).is_err());
        assert!(
            fixture
                .0
                .join("WebView2Runtime/webview2-runtime.json")
                .exists()
        );
        drop(locked);
        assert_eq!(remove_installer_files(&fixture.0).unwrap(), 21);
    }

    #[test]
    fn partial_cleanup_is_completed_without_requiring_installer_again() {
        let fixture = Fixture::new();
        fs::remove_file(fixture.installer()).unwrap();
        assert_eq!(remove_installer_files(&fixture.0).unwrap(), 14);
    }

    #[test]
    fn junction_to_another_directory_is_never_traversed() {
        let fixture = Fixture::new();
        let linked_root = fixture.0.join("linked-app");
        fs::create_dir(&linked_root).unwrap();
        let junction = linked_root.join("WebView2Runtime");
        let status = std::process::Command::new("cmd")
            .args(["/d", "/c", "mklink", "/J"])
            .arg(&junction)
            .arg(fixture.0.join("WebView2Runtime"))
            .stdout(std::process::Stdio::null())
            .status()
            .unwrap();
        assert!(status.success());
        assert!(remove_installer_files(&linked_root).is_err());
        assert!(fixture.installer().exists());
        fs::remove_dir(junction).unwrap();
    }
}
