use std::{error::Error, path::Path};

pub(crate) fn reject_network_data_root(path: &Path) -> Result<(), Box<dyn Error>> {
    if is_network_data_root(path)? {
        return Err(format!(
            "业务数据目录 '{}' 位于网络文件系统。SQLite、缓存和桌面运行数据必须放在本机磁盘；请改选本地 NTFS、APFS、ext4 等文件系统。",
            path.display()
        )
        .into());
    }
    Ok(())
}

#[cfg(windows)]
fn is_network_data_root(path: &Path) -> Result<bool, Box<dyn Error>> {
    use std::path::Prefix;
    use windows_sys::Win32::Storage::FileSystem::GetDriveTypeW;

    const DRIVE_REMOTE: u32 = 4;
    let Some(prefix) = path
        .components()
        .next()
        .and_then(|component| match component {
            std::path::Component::Prefix(prefix) => Some(prefix.kind()),
            _ => None,
        })
    else {
        return Ok(false);
    };

    match prefix {
        Prefix::UNC(..) | Prefix::VerbatimUNC(..) => Ok(true),
        Prefix::Disk(letter) | Prefix::VerbatimDisk(letter) => {
            let root = [u16::from(letter), u16::from(b':'), u16::from(b'\\'), 0];
            Ok(unsafe { GetDriveTypeW(root.as_ptr()) } == DRIVE_REMOTE)
        }
        _ => Ok(false),
    }
}

#[cfg(unix)]
fn is_network_data_root(path: &Path) -> Result<bool, Box<dyn Error>> {
    let existing_path = nearest_existing_ancestor(path)?;
    network_file_system_for_existing_path(existing_path)
}

#[cfg(not(any(unix, windows)))]
fn is_network_data_root(_path: &Path) -> Result<bool, Box<dyn Error>> {
    Ok(false)
}

#[cfg(unix)]
fn nearest_existing_ancestor(path: &Path) -> Result<&Path, Box<dyn Error>> {
    let mut candidate = path;
    while !candidate.exists() {
        candidate = candidate
            .parent()
            .ok_or_else(|| format!("无法定位数据目录 '{}' 所在的文件系统。", path.display()))?;
    }
    Ok(candidate)
}

#[cfg(target_os = "linux")]
fn network_file_system_for_existing_path(path: &Path) -> Result<bool, Box<dyn Error>> {
    use std::{ffi::CString, mem::MaybeUninit, os::unix::ffi::OsStrExt};

    let native_path = CString::new(path.as_os_str().as_bytes())
        .map_err(|_| format!("数据目录包含不支持的空字符：'{}'.", path.display()))?;
    let mut status = MaybeUninit::<libc::statfs>::zeroed();
    if unsafe { libc::statfs(native_path.as_ptr(), status.as_mut_ptr()) } != 0 {
        return Err(std::io::Error::last_os_error().into());
    }
    let file_system_type = unsafe { status.assume_init() }.f_type as u64;
    Ok(is_network_file_system_magic(file_system_type))
}

#[cfg(target_os = "macos")]
fn network_file_system_for_existing_path(path: &Path) -> Result<bool, Box<dyn Error>> {
    use std::{ffi::CString, mem::MaybeUninit, os::unix::ffi::OsStrExt};

    let native_path = CString::new(path.as_os_str().as_bytes())
        .map_err(|_| format!("数据目录包含不支持的空字符：'{}'.", path.display()))?;
    let mut status = MaybeUninit::<libc::statfs>::zeroed();
    if unsafe { libc::statfs(native_path.as_ptr(), status.as_mut_ptr()) } != 0 {
        return Err(std::io::Error::last_os_error().into());
    }
    let status = unsafe { status.assume_init() };
    let file_system_name = status
        .f_fstypename
        .iter()
        .take_while(|value| **value != 0)
        .map(|value| *value as u8)
        .collect::<Vec<_>>();
    Ok(is_network_file_system_name(&String::from_utf8_lossy(
        &file_system_name,
    )))
}

#[cfg(all(unix, not(any(target_os = "linux", target_os = "macos"))))]
fn network_file_system_for_existing_path(_path: &Path) -> Result<bool, Box<dyn Error>> {
    Ok(false)
}

#[cfg(any(target_os = "macos", test))]
pub(crate) fn is_network_file_system_name(value: &str) -> bool {
    matches!(
        value.trim().to_ascii_lowercase().as_str(),
        "nfs" | "nfs4" | "smbfs" | "cifs" | "webdav" | "afpfs"
    )
}

#[cfg(any(target_os = "linux", test))]
pub(crate) fn is_network_file_system_magic(value: u64) -> bool {
    matches!(
        value,
        0x0000_6969 // NFS
            | 0xFE53_4D42 // SMB2
            | 0x0000_517B // SMB
            | 0xFF53_4D42 // CIFS
    )
}
