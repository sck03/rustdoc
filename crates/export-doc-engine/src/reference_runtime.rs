use crate::{
    api::ApiClient,
    paths::{RuntimePaths, ensure_safe_absolute, nonce},
};
use serde::Deserialize;
use std::{
    fs,
    process::{Child, Command, Stdio},
    thread,
    time::{Duration, Instant},
};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Endpoint {
    schema_version: u32,
    process_id: u32,
    api_base_url: String,
}

pub struct Runtime {
    child: Child,
    pub client: ApiClient,
    pub paths: RuntimePaths,
    #[cfg(windows)]
    job: ProcessJob,
}
impl Runtime {
    pub fn start(paths: RuntimePaths) -> Result<Self, String> {
        let executable = paths.app_root.join("sidecar").join(if cfg!(windows) {
            "ExportDocManager.Api.exe"
        } else {
            "ExportDocManager.Api"
        });
        ensure_safe_absolute(&executable)?;
        let run_id = nonce()?;
        let endpoint_file = paths.cache_root.join(format!("endpoint-{run_id}.json"));
        let token = format!("{}{}", nonce()?, nonce()?);
        let stdout = fs::File::create(
            paths
                .log_root
                .join(format!("native-api-{run_id}.stdout.log")),
        )
        .map_err(|error| error.to_string())?;
        let stderr = fs::File::create(
            paths
                .log_root
                .join(format!("native-api-{run_id}.stderr.log")),
        )
        .map_err(|error| error.to_string())?;
        let temp_root = paths.cache_root.join("Temp");
        fs::create_dir_all(&temp_root).map_err(|error| error.to_string())?;
        let mut command = Command::new(executable);
        command
            .args(["--urls", "http://127.0.0.1:0", "--product-edition", "Full"])
            .arg("--app-root")
            .arg(&paths.app_root)
            .arg("--data-root")
            .arg(&paths.data_root)
            .arg("--endpoint-file")
            .arg(&endpoint_file)
            .env("EXPORTDOCMANAGER_DESKTOP_TOKEN", &token)
            .env("EXPORTDOCMANAGER_DATA_ROOT", &paths.data_root)
            .env("TEMP", &temp_root)
            .env("TMP", &temp_root)
            .env("Logging__LogLevel__Default", "Warning")
            .current_dir(&paths.app_root)
            .stdin(Stdio::null())
            .stdout(stdout)
            .stderr(stderr);
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000);
        }
        let mut child = command
            .spawn()
            .map_err(|error| format!("C# 后端启动失败：{error}"))?;
        #[cfg(windows)]
        let job = match ProcessJob::attach(&child) {
            Ok(job) => job,
            Err(error) => {
                let _ = child.kill();
                let _ = child.wait();
                return Err(error);
            }
        };
        let startup = (|| {
            let deadline = Instant::now() + Duration::from_secs(35);
            while Instant::now() < deadline {
                if let Some(status) = child.try_wait().map_err(|error| error.to_string())? {
                    return Err(format!(
                        "C# 后端提前退出（{status}），请查看 {}。",
                        paths.log_root.display()
                    ));
                }
                match fs::read(&endpoint_file) {
                    Ok(data) => {
                        ensure_safe_absolute(&endpoint_file)?;
                        if data.len() > 4096 {
                            return Err("后端端点文件超过容量上限。".into());
                        }
                        let text = std::str::from_utf8(&data)
                            .map_err(|error| error.to_string())?
                            .trim_start_matches('\u{feff}');
                        let publication: Endpoint = serde_json::from_str(text)
                            .map_err(|error| format!("后端端点文件无效：{error}"))?;
                        if publication.schema_version != 1 || publication.process_id != child.id() {
                            return Err("后端端点文件不属于本次启动的进程。".into());
                        }
                        let client = ApiClient::new(&publication.api_base_url, token.clone())
                            .map_err(|error| error.to_string())?;
                        let _: serde_json::Value = client
                            .json(crate::generated_api::GET_HEALTH, &[], &[], None)
                            .map_err(|error| error.to_string())?;
                        return Ok(client);
                    }
                    Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                    Err(error) => return Err(format!("后端端点文件不可读：{error}")),
                }
                thread::sleep(Duration::from_millis(150));
            }
            Err(format!(
                "后端未在 35 秒内就绪，请查看 {}。",
                paths.log_root.display()
            ))
        })();
        let _ = fs::remove_file(&endpoint_file);
        match startup {
            Ok(client) => Ok(Self {
                child,
                client,
                paths,
                #[cfg(windows)]
                job,
            }),
            Err(error) => {
                let _ = child.kill();
                let _ = child.wait();
                Err(error)
            }
        }
    }
    pub fn process_id(&self) -> u32 {
        self.child.id()
    }
    pub fn shutdown(&mut self) -> Result<(), String> {
        self.client
            .shutdown_maintenance()
            .map_err(|error| error.to_string())?;
        let deadline = Instant::now() + Duration::from_secs(15);
        while Instant::now() < deadline {
            if self
                .child
                .try_wait()
                .map_err(|error| error.to_string())?
                .is_some()
            {
                return Ok(());
            }
            thread::sleep(Duration::from_millis(100));
        }
        Err("后端未在 15 秒内完成关闭，将清理本次进程树。".into())
    }
}
impl Drop for Runtime {
    fn drop(&mut self) {
        #[cfg(windows)]
        self.job.terminate();
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[cfg(windows)]
pub(crate) struct ProcessJob(windows::Win32::Foundation::HANDLE);
#[cfg(windows)]
impl ProcessJob {
    pub(crate) fn attach(child: &Child) -> Result<Self, String> {
        use std::{mem::size_of, os::windows::io::AsRawHandle};
        use windows::Win32::{Foundation::HANDLE, System::JobObjects::*};
        // SAFETY: Windows owns both live handles; the job is closed exactly once
        // by this RAII guard. Assignment happens before any API work is issued.
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
    fn terminate(&self) {
        // SAFETY: this handle stays valid until Drop and owns only our sidecar tree.
        unsafe {
            let _ = windows::Win32::System::JobObjects::TerminateJobObject(self.0, 0);
        }
    }
}
#[cfg(windows)]
impl Drop for ProcessJob {
    fn drop(&mut self) {
        unsafe {
            let _ = windows::Win32::Foundation::CloseHandle(self.0);
        }
    }
}
