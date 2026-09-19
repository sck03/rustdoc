use crate::{api::ApiClient, paths::RuntimePaths};

pub struct Runtime {
    pub client: ApiClient,
    pub paths: RuntimePaths,
}
impl Runtime {
    pub fn start(paths: RuntimePaths) -> Result<Self, String> {
        Self::start_with_retention(paths, Default::default())
    }
    pub fn start_with_retention(
        paths: RuntimePaths,
        retention: crate::engine::tasks::retention::Retention,
    ) -> Result<Self, String> {
        let client = ApiClient::native_with_retention(paths.clone(), retention)
            .map_err(|error| error.to_string())?;
        Ok(Self { client, paths })
    }
    pub fn process_id(&self) -> u32 {
        std::process::id()
    }
    pub fn shutdown(&mut self) -> Result<(), String> {
        self.client
            .shutdown_maintenance()
            .map_err(|error| error.to_string())
    }
}

#[cfg(windows)]
pub(crate) struct ProcessJob(windows::Win32::Foundation::HANDLE);
#[cfg(windows)]
impl ProcessJob {
    pub(crate) fn attach(child: &std::process::Child) -> Result<Self, String> {
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
#[cfg(windows)]
impl Drop for ProcessJob {
    fn drop(&mut self) {
        // SAFETY: This unique handle is valid until this guard is dropped.
        unsafe {
            let _ = windows::Win32::Foundation::CloseHandle(self.0);
        }
    }
}
