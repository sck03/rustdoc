//! Host volume identity stays at the platform/path boundary, outside business rules.
use std::path::Path;

pub(crate) fn identity(path: &Path) -> Result<String, String> {
    super::ensure_safe_absolute(path)?;
    let mut existing = path;
    while !existing
        .try_exists()
        .map_err(|_| "无法检查存储介质，请核对挂载与目录权限。")?
    {
        existing = existing.parent().ok_or("存储介质不可用。")?;
    }
    platform(existing)
}
#[cfg(windows)]
fn platform(path: &Path) -> Result<String, String> {
    use std::os::windows::{fs::OpenOptionsExt, io::AsRawHandle};
    use windows::Win32::{
        Foundation::HANDLE,
        Storage::FileSystem::{
            FILE_FLAG_BACKUP_SEMANTICS, FILE_ID_INFO, FileIdInfo, GetFileInformationByHandleEx,
        },
    };
    let file = std::fs::OpenOptions::new()
        .read(true)
        .custom_flags(FILE_FLAG_BACKUP_SEMANTICS.0)
        .open(path)
        .map_err(|_| "无法打开存储介质，请核对挂载与读取权限。")?;
    let mut info = FILE_ID_INFO::default();
    // The owned File keeps the handle valid; the output is exactly FILE_ID_INFO's size.
    unsafe {
        GetFileInformationByHandleEx(
            HANDLE(file.as_raw_handle()),
            FileIdInfo,
            (&mut info as *mut FILE_ID_INFO).cast(),
            std::mem::size_of::<FILE_ID_INFO>() as u32,
        )
    }
    .map_err(|_| "目标文件系统无法提供可靠介质身份，请使用支持身份核验的存储设备。")?;
    if info.VolumeSerialNumber == 0 {
        return Err("目标未提供有效卷身份，不能登记为受管存储。".into());
    }
    Ok(format!("win-volume:{:016x}", info.VolumeSerialNumber))
}
#[cfg(unix)]
fn platform(path: &Path) -> Result<String, String> {
    use std::os::unix::ffi::OsStrExt;
    let name =
        std::ffi::CString::new(path.as_os_str().as_bytes()).map_err(|_| "存储路径含空字符。")?;
    let mut info = std::mem::MaybeUninit::<libc::statfs>::uninit();
    // statfs returns a filesystem identity, unlike st_dev which can change when
    // the same device is remounted. The pointer lives for the synchronous call.
    if unsafe { libc::statfs(name.as_ptr(), info.as_mut_ptr()) } != 0 {
        return Err("无法核对存储文件系统身份，请检查挂载和读取权限。".into());
    }
    let info = unsafe { info.assume_init() };
    let bytes = unsafe {
        std::slice::from_raw_parts(
            (&info.f_fsid as *const libc::fsid_t).cast::<u8>(),
            std::mem::size_of::<libc::fsid_t>(),
        )
    };
    if bytes.iter().all(|b| *b == 0) {
        return Err("目标文件系统未提供有效身份，不能登记为外部受管介质。".into());
    }
    Ok(format!("unix-filesystem:{}", super::hex(bytes)))
}
#[cfg(not(any(unix, windows)))]
fn platform(_: &Path) -> Result<String, String> {
    Err("此平台尚未实现存储介质身份核验。".into())
}
