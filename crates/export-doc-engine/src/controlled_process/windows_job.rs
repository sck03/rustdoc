//! Windows job handle for the controlled tool process tree.
pub(super) struct ProcessJob(windows::Win32::Foundation::HANDLE);
impl ProcessJob {
    pub(super) fn attach(child: &std::process::Child) -> Result<Self, String> {
        use std::{mem::size_of, os::windows::io::AsRawHandle};
        use windows::Win32::{Foundation::HANDLE, System::JobObjects::*};
        // SAFETY: The live job owns only this bounded decoder process tree.
        unsafe {
            let guard = Self(CreateJobObjectW(None, None).map_err(|error| error.to_string())?);
            let mut limits = JOBOBJECT_EXTENDED_LIMIT_INFORMATION::default();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            SetInformationJobObject(
                guard.0,
                JobObjectExtendedLimitInformation,
                &limits as *const _ as *const _,
                size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
            .map_err(|error| error.to_string())?;
            AssignProcessToJobObject(guard.0, HANDLE(child.as_raw_handle()))
                .map_err(|error| error.to_string())?;
            Ok(guard)
        }
    }
}
impl Drop for ProcessJob {
    fn drop(&mut self) {
        // SAFETY: This unique handle is valid until this guard is dropped.
        unsafe {
            let _ = windows::Win32::Foundation::CloseHandle(self.0);
        }
    }
}
