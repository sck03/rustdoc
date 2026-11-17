//! Existing host paths are compared using the platform's path identity rules.
use std::{fs, path::Path};

pub(super) fn overlaps(left: &Path, right: &Path) -> Result<bool, String> {
    let left = fs::canonicalize(left).map_err(|error| error.to_string())?;
    let right = fs::canonicalize(right).map_err(|error| error.to_string())?;
    // Equal prefixes in either direction mean the roots share a managed subtree.
    Ok(left.components().zip(right.components()).all(|(a, b)| {
        #[cfg(windows)]
        {
            use std::os::windows::ffi::OsStrExt;
            use windows::Win32::Globalization::{CSTR_EQUAL, CompareStringOrdinal};
            let a: Vec<_> = a.as_os_str().encode_wide().collect();
            let b: Vec<_> = b.as_os_str().encode_wide().collect();
            unsafe { CompareStringOrdinal(&a, &b, true) == CSTR_EQUAL }
        }
        #[cfg(not(windows))]
        {
            a == b
        }
    }))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn existing_roots_reject_overlap_and_preserve_distinct_directories() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let app = root.0.join("Application");
        let data = app.join("Data");
        let independent = root.0.join("ApplicationArchive");
        fs::create_dir_all(&data).unwrap();
        fs::create_dir_all(&independent).unwrap();
        assert!(overlaps(&app, &data).unwrap());
        assert!(overlaps(&data, &app).unwrap());
        assert!(!overlaps(&app, &independent).unwrap());
        #[cfg(windows)]
        assert!(overlaps(&app, &root.0.join("APPLICATION").join("DATA")).unwrap());
        #[cfg(not(windows))]
        {
            let different = root.0.join("APPLICATION");
            fs::create_dir_all(&different).unwrap();
            if fs::canonicalize(&different).unwrap() != fs::canonicalize(&app).unwrap() {
                assert!(!overlaps(&app, &different).unwrap());
            }
        }
    }
}
