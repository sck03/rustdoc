use std::{
    env,
    path::{Component, Path, PathBuf},
};

/// Returns a lexical path identity suitable for security and migration decisions.
/// Windows file-system names are case-insensitive; Unix-like systems retain their
/// normal case-sensitive semantics. The function does not touch the file system, so
/// it is safe to use before a target directory exists.
pub(crate) fn same_path(left: &Path, right: &Path) -> bool {
    identity_components(left) == identity_components(right)
}

pub(crate) fn is_path_within(path: &Path, root: &Path) -> bool {
    let path_components = identity_components(path);
    let root_components = identity_components(root);
    path_components.len() >= root_components.len()
        && path_components[..root_components.len()] == root_components
}

pub(crate) fn identity_components(path: &Path) -> Vec<String> {
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        env::current_dir()
            .unwrap_or_else(|_| PathBuf::from("."))
            .join(path)
    };

    let mut components: Vec<String> = Vec::new();
    for component in absolute.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                if matches!(components.last(), Some(value) if value != "/" && value != "\\" && !value.ends_with(':'))
                {
                    components.pop();
                }
            }
            Component::Prefix(_) | Component::RootDir | Component::Normal(_) => {
                let value = component.as_os_str().to_string_lossy().into_owned();
                #[cfg(windows)]
                let value = value.to_lowercase();
                components.push(value);
            }
        }
    }
    components
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(windows)]
    #[test]
    fn windows_identity_ignores_drive_and_segment_case() {
        assert!(same_path(
            Path::new(r"D:\Data\Exports"),
            Path::new(r"d:\data\exports"),
        ));
        assert!(is_path_within(
            Path::new(r"D:\Data\Exports\2026"),
            Path::new(r"d:\data"),
        ));
    }

    #[cfg(not(windows))]
    #[test]
    fn unix_identity_remains_case_sensitive() {
        assert!(!same_path(Path::new("/Data"), Path::new("/data")));
        assert!(is_path_within(
            Path::new("/data/exports"),
            Path::new("/data")
        ));
    }
}
