//! Shared persisted-path rules for settings and desktop directory dialogs.
use super::ensure_safe_absolute;
use std::{
    fs,
    path::{Path, PathBuf},
};

pub fn resolve_managed_relative(root: &Path, value: &str) -> Result<PathBuf, String> {
    if value.is_empty()
        || value.contains(':')
        || value
            .split(['/', '\\'])
            .any(|part| part.is_empty() || part == "." || part == "..")
    {
        return Err("配置路径必须是数据目录内的相对路径。".into());
    }
    let path = value
        .split(['/', '\\'])
        .fold(root.to_path_buf(), |path, part| path.join(part));
    ensure_safe_absolute(&path)?;
    Ok(path)
}

pub fn managed_relative_directory(root: &Path, selected: &Path) -> Result<String, String> {
    ensure_safe_absolute(root)?;
    ensure_safe_absolute(selected)?;
    if !fs::metadata(selected)
        .map_err(|error| error.to_string())?
        .is_dir()
    {
        return Err("请选择已有目录。".into());
    }
    let root = fs::canonicalize(root).map_err(|error| error.to_string())?;
    let selected = fs::canonicalize(selected).map_err(|error| error.to_string())?;
    let roots: Vec<_> = root.components().collect();
    let parts: Vec<_> = selected.components().collect();
    let equal = |left: &std::path::Component<'_>, right: &std::path::Component<'_>| {
        #[cfg(windows)]
        {
            left.as_os_str().to_string_lossy().to_lowercase()
                == right.as_os_str().to_string_lossy().to_lowercase()
        }
        #[cfg(not(windows))]
        {
            left == right
        }
    };
    if parts.len() <= roots.len()
        || !roots
            .iter()
            .zip(&parts)
            .all(|(left, right)| equal(left, right))
    {
        return Err("默认导出目录须位于运行数据根目录内，请选择其中的子目录。".into());
    }
    let relative = parts[roots.len()..]
        .iter()
        .map(|part| part.as_os_str().to_str().ok_or("目录名不是有效 UTF-8。"))
        .collect::<Result<Vec<_>, _>>()?
        .join("/");
    resolve_managed_relative(&root, &relative)?;
    Ok(relative)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::engine::test_support::ScratchDirectory;

    #[test]
    fn selected_directory_round_trips_without_using_working_directory() {
        let scratch = ScratchDirectory::new();
        let root = scratch.paths().data_root;
        let selected = root.join("Exports").join("发票");
        fs::create_dir_all(&selected).unwrap();
        let relative = managed_relative_directory(&root, &selected).unwrap();
        assert_eq!(relative, "Exports/发票");
        assert_eq!(
            resolve_managed_relative(&root, &relative).unwrap(),
            selected
        );
        assert!(managed_relative_directory(&root, &root).is_err());
        assert!(managed_relative_directory(&root, root.parent().unwrap()).is_err());
        for unsafe_value in [
            "../out",
            "Exports/../out",
            "Exports\\..\\out",
            "/out",
            "\\out",
            "C:/out",
            ".",
            "",
        ] {
            assert!(
                resolve_managed_relative(&root, unsafe_value).is_err(),
                "{unsafe_value}"
            );
        }
    }
}
