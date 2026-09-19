use std::path::Path;

pub fn directory(path: &Path) -> Result<(), String> {
    crate::paths::ensure_safe_absolute(path)?;
    std::fs::create_dir_all(path).map_err(|_| "无法创建凭证安全目录。")?;
    crate::paths::ensure_safe_absolute(path)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700))
            .map_err(|_| "无法限制凭证目录访问权限。")?;
    }
    #[cfg(windows)]
    restrict(path)?;
    Ok(())
}

#[cfg(windows)]
fn restrict(path: &Path) -> Result<(), String> {
    use std::os::windows::ffi::OsStrExt;
    use windows::{
        Win32::{
            Foundation::{HLOCAL, LocalFree},
            Security::{
                Authorization::{
                    ConvertStringSecurityDescriptorToSecurityDescriptorW, SE_FILE_OBJECT,
                    SetNamedSecurityInfoW,
                },
                DACL_SECURITY_INFORMATION, GetSecurityDescriptorDacl,
                PROTECTED_DACL_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR,
            },
        },
        core::{BOOL, PCWSTR, w},
    };
    // OWNER RIGHTS grants access to the actual owner and is inherited by files.
    // The protected DACL stops broad permissions inherited from a shared data drive.
    let name: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
    unsafe {
        let mut descriptor = PSECURITY_DESCRIPTOR::default();
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            w!("D:P(A;OICI;FA;;;OW)"),
            1,
            &mut descriptor,
            None,
        )
        .map_err(|_| "无法建立凭证目录访问规则。")?;
        let result = (|| {
            let mut present = BOOL::default();
            let mut defaulted = BOOL::default();
            let mut acl = std::ptr::null_mut();
            GetSecurityDescriptorDacl(descriptor, &mut present, &mut acl, &mut defaulted)
                .map_err(|_| "无法读取凭证目录访问规则。")?;
            if !present.as_bool() || acl.is_null() {
                return Err("凭证目录访问规则为空。".to_owned());
            }
            SetNamedSecurityInfoW(
                PCWSTR(name.as_ptr()),
                SE_FILE_OBJECT,
                DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                None,
                None,
                Some(acl),
                None,
            )
            .ok()
            .map_err(|_| "无法限制凭证目录访问权限。".to_owned())
        })();
        let _ = LocalFree(Some(HLOCAL(descriptor.0)));
        result
    }
}
