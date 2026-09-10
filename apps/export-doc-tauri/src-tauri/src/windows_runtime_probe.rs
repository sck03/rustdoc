use std::{
    os::windows::ffi::OsStrExt,
    path::{Path, PathBuf},
};
use windows_sys::Win32::{
    Foundation::FreeLibrary,
    System::{
        LibraryLoader::{
            LoadLibraryExW, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR, LOAD_LIBRARY_SEARCH_SYSTEM32,
        },
        Registry::{RegGetValueW, HKEY_LOCAL_MACHINE, RRF_RT_REG_SZ, RRF_SUBKEY_WOW6464KEY},
    },
};

pub(crate) fn check_windows_version() -> Result<(), String> {
    let key: Vec<u16> = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\0"
        .encode_utf16()
        .collect();
    let value: Vec<u16> = "CurrentBuildNumber\0".encode_utf16().collect();
    let mut buffer = [0u16; 32];
    let mut bytes = std::mem::size_of_val(&buffer) as u32;
    let result = unsafe {
        RegGetValueW(
            HKEY_LOCAL_MACHINE,
            key.as_ptr(),
            value.as_ptr(),
            RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
            std::ptr::null_mut(),
            buffer.as_mut_ptr().cast(),
            &mut bytes,
        )
    };
    if result != 0 {
        return Err(format!(
            "无法读取 Windows 版本：{}",
            std::io::Error::from_raw_os_error(result as i32)
        ));
    }
    let length = (bytes as usize / std::mem::size_of::<u16>()).min(buffer.len());
    let build = String::from_utf16(&buffer[..length])
        .map_err(|error| format!("Windows 版本无效：{error}"))?
        .trim_end_matches('\0')
        .parse::<u32>()
        .map_err(|error| format!("Windows 版本无效：{error}"))?;
    if !windows_build_is_supported(build) {
        return Err(format!("当前 Windows 内部版本为 {build}。本程序需要 Windows 10 1809／Server 2019（内部版本 17763）或更高版本。"));
    }
    Ok(())
}

fn windows_build_is_supported(build: u32) -> bool {
    build >= 17_763
}

pub(crate) fn ocr_library_path(root: &Path) -> PathBuf {
    root.join("sidecar").join("onnxruntime.dll")
}

pub(crate) fn check_packaged_ocr(root: &Path) -> Result<(), String> {
    if env!("EXPORTDOCMANAGER_HAS_OCR") != "true" || !root.join("sidecar").is_dir() {
        return Ok(());
    }
    if cfg!(target_arch = "x86_64") {
        for name in [
            "msvc-runtime.json",
            "vcruntime140.dll",
            "vcruntime140_1.dll",
            "msvcp140.dll",
            "msvcp140_1.dll",
        ] {
            if !root.join("sidecar").join(name).is_file() {
                return Err(format!(
                    "程序包缺少 OCR 运行组件 {name}，请重新解压完整程序包或重新安装。"
                ));
            }
        }
    }
    if !ocr_library_available(root)? {
        return Err("随包 OCR 组件缺少必需的本地依赖，请重新解压完整程序包或重新安装。无需另外安装整套 Visual C++ 运行库。".into());
    }
    Ok(())
}

pub(crate) fn ocr_library_available(root: &Path) -> Result<bool, String> {
    let library = ocr_library_path(root);
    let name: Vec<u16> = library
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    // Probe the actual packaged imports before starting the API and opening the window.
    // Search only the DLL's directory and Windows system libraries, never PATH/CWD.
    let handle = unsafe {
        LoadLibraryExW(
            name.as_ptr(),
            std::ptr::null_mut(),
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32,
        )
    };
    if !handle.is_null() {
        unsafe { FreeLibrary(handle) };
        return Ok(true);
    }
    let error = std::io::Error::last_os_error();
    if missing_native_import(error.raw_os_error()) {
        return Ok(false);
    }
    Err(format!(
        "无法加载随包 OCR 组件 '{}'：{error}。请检查程序包是否完整且架构正确。",
        library.display()
    ))
}

fn missing_native_import(code: Option<i32>) -> bool {
    matches!(code, Some(126 | 127))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn minimum_windows_build_is_1809() {
        assert!(!windows_build_is_supported(17_762));
        assert!(windows_build_is_supported(17_763));
        assert!(windows_build_is_supported(22_000));
    }

    #[test]
    fn only_missing_dlls_or_exports_request_runtime_installation() {
        assert!(missing_native_import(Some(126)));
        assert!(missing_native_import(Some(127)));
        for code in [Some(5), Some(193), Some(577), None] {
            assert!(!missing_native_import(code));
        }
    }
}
