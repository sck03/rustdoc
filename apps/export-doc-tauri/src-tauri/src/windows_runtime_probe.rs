use windows_sys::Win32::System::Registry::{
    HKEY_LOCAL_MACHINE, RRF_RT_REG_SZ, RRF_SUBKEY_WOW6464KEY, RegGetValueW,
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
        return Err(format!(
            "当前 Windows 内部版本为 {build}。本程序需要 Windows 10 1809／Server 2019（内部版本 17763）或更高版本。"
        ));
    }
    Ok(())
}

fn windows_build_is_supported(build: u32) -> bool {
    build >= 17_763
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
}
