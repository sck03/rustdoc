use std::{error::Error, fs, io::Read, path::Path};

use sha2::{Digest, Sha256};

#[derive(Clone, Debug, PartialEq, Eq, serde::Deserialize, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct TreeManifest {
    pub(crate) file_count: u64,
    pub(crate) directory_count: u64,
    pub(crate) total_bytes: u64,
    pub(crate) sha256: String,
}

pub(crate) fn collect_tree_manifest(
    root: &Path,
    excluded_root_name: Option<&str>,
) -> Result<TreeManifest, Box<dyn Error>> {
    let mut state = ManifestState::default();
    collect_directory(root, root, excluded_root_name, &mut state)?;
    Ok(TreeManifest {
        file_count: state.file_count,
        directory_count: state.directory_count,
        total_bytes: state.total_bytes,
        sha256: format!("{:x}", state.tree_hasher.finalize()),
    })
}

#[derive(Default)]
struct ManifestState {
    file_count: u64,
    directory_count: u64,
    total_bytes: u64,
    tree_hasher: Sha256,
}

fn collect_directory(
    root: &Path,
    directory: &Path,
    excluded_root_name: Option<&str>,
    state: &mut ManifestState,
) -> Result<(), Box<dyn Error>> {
    let mut entries = fs::read_dir(directory)?.collect::<Result<Vec<_>, _>>()?;
    entries.sort_by_key(|entry| entry.file_name());

    for entry in entries {
        if directory == root && excluded_root_name.is_some_and(|name| entry.file_name() == name) {
            continue;
        }

        let entry_path = entry.path();
        let metadata = fs::symlink_metadata(&entry_path)?;
        if metadata.file_type().is_symlink() || is_windows_reparse_point(&metadata) {
            return Err(format!(
                "Link-like entry '{}' is not supported.",
                entry_path.display()
            )
            .into());
        }

        let relative = normalized_relative_path(root, &entry_path)?;
        if metadata.is_dir() {
            state.directory_count = state.directory_count.saturating_add(1);
            update_tree_digest(&mut state.tree_hasher, b'D', &relative, 0, &[]);
            collect_directory(root, &entry_path, excluded_root_name, state)?;
        } else if metadata.is_file() {
            let content_digest = hash_file(&entry_path)?;
            state.file_count = state.file_count.saturating_add(1);
            state.total_bytes = state.total_bytes.saturating_add(metadata.len());
            update_tree_digest(
                &mut state.tree_hasher,
                b'F',
                &relative,
                metadata.len(),
                &content_digest,
            );
        } else {
            return Err(format!("Unsupported filesystem entry '{}'.", entry_path.display()).into());
        }
    }

    Ok(())
}

fn normalized_relative_path(root: &Path, path: &Path) -> Result<String, Box<dyn Error>> {
    Ok(path
        .strip_prefix(root)?
        .components()
        .map(|component| component.as_os_str().to_string_lossy())
        .collect::<Vec<_>>()
        .join("/"))
}

fn hash_file(path: &Path) -> Result<Vec<u8>, Box<dyn Error>> {
    let mut file = fs::File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hasher.finalize().to_vec())
}

fn update_tree_digest(
    hasher: &mut Sha256,
    entry_type: u8,
    relative_path: &str,
    length: u64,
    content_digest: &[u8],
) {
    hasher.update([entry_type]);
    hasher.update((relative_path.len() as u64).to_le_bytes());
    hasher.update(relative_path.as_bytes());
    hasher.update(length.to_le_bytes());
    hasher.update(content_digest);
}

#[cfg(windows)]
fn is_windows_reparse_point(metadata: &fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
    metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0
}

#[cfg(not(windows))]
fn is_windows_reparse_point(_metadata: &fs::Metadata) -> bool {
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn manifest_detects_same_length_content_changes() {
        let root = fresh_test_dir("same-size-change");
        fs::create_dir_all(root.join("Database")).unwrap();
        fs::write(root.join("Database").join("data.db"), b"first").unwrap();
        let first = collect_tree_manifest(&root, None).unwrap();

        fs::write(root.join("Database").join("data.db"), b"other").unwrap();
        let second = collect_tree_manifest(&root, None).unwrap();

        assert_eq!(first.file_count, second.file_count);
        assert_eq!(first.total_bytes, second.total_bytes);
        assert_ne!(first.sha256, second.sha256);
        fs::remove_dir_all(root).unwrap();
    }

    fn fresh_test_dir(name: &str) -> std::path::PathBuf {
        let root = std::env::current_dir()
            .unwrap()
            .join("target")
            .join("runtime-tree-manifest-tests")
            .join(format!("{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        root
    }
}
