//! Directory-link fixtures share native Windows command path handling.
use std::path::{Path, PathBuf};

fn command_path(path: &Path) -> PathBuf {
    std::path::absolute(path)
        .expect("absolute fixture path")
        .components()
        .collect()
}

pub(crate) fn create(target: &Path, link: &Path) {
    let status = std::process::Command::new("cmd")
        .args(["/d", "/c", "mklink", "/J"])
        .arg(command_path(link))
        .arg(command_path(target))
        .status()
        .expect("create directory junction");
    assert!(
        status.success(),
        "directory junction creation failed: {status}"
    );
}

#[test]
fn mixed_separators_and_spaces_use_native_command_paths() {
    let path = command_path(Path::new("C:/fixture root/mixed\\directory"));
    assert_eq!(path.as_os_str(), "C:\\fixture root\\mixed\\directory");
}
