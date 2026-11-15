//! Identity and mutation stamps from an open handle, independent of path spelling.
//! Access time is deliberately excluded so reading never invalidates verification.
use std::{fs::File, io};

#[derive(Clone, PartialEq, Eq)]
pub(crate) struct Stamp {
    pub(crate) size: u64,
    identity: [u64; 2],
    changed: [i64; 4],
}

#[cfg(unix)]
pub(crate) fn read(file: &File) -> io::Result<Stamp> {
    use std::os::unix::fs::MetadataExt;
    let info = file.metadata()?;
    Ok(Stamp {
        size: info.len(),
        identity: [info.dev(), info.ino()],
        changed: [
            info.mtime(),
            info.mtime_nsec(),
            info.ctime(),
            info.ctime_nsec(),
        ],
    })
}

#[cfg(windows)]
pub(crate) fn read(file: &File) -> io::Result<Stamp> {
    use std::{mem::size_of, os::windows::io::AsRawHandle};
    use windows::Win32::{Foundation::HANDLE, Storage::FileSystem::*};
    let handle = HANDLE(file.as_raw_handle());
    let mut identity = BY_HANDLE_FILE_INFORMATION::default();
    let mut times = FILE_BASIC_INFO::default();
    // SAFETY: Both output buffers have the matching API type and size, and the
    // borrowed file keeps the handle alive for both calls.
    unsafe {
        GetFileInformationByHandle(handle, &mut identity).map_err(io::Error::other)?;
        GetFileInformationByHandleEx(
            handle,
            FileBasicInfo,
            &mut times as *mut _ as *mut _,
            size_of::<FILE_BASIC_INFO>() as u32,
        )
        .map_err(io::Error::other)?;
    }
    Ok(Stamp {
        size: (u64::from(identity.nFileSizeHigh) << 32) | u64::from(identity.nFileSizeLow),
        identity: [
            u64::from(identity.dwVolumeSerialNumber),
            (u64::from(identity.nFileIndexHigh) << 32) | u64::from(identity.nFileIndexLow),
        ],
        changed: [
            times.CreationTime,
            times.LastWriteTime,
            times.ChangeTime,
            i64::from(times.FileAttributes),
        ],
    })
}
