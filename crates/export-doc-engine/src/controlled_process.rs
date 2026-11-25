//! A bounded native process tree with cancellable pipes. No shell interpolation.
#[cfg(feature = "ocr")]
mod frames;
#[cfg(windows)]
mod windows_job;
#[cfg(feature = "ocr")]
pub(crate) use frames::FramedProcess;

use crate::{api::ApiError, operation};
use std::{
    io::Read,
    process::{Child, Command, ExitStatus, Stdio},
    sync::mpsc,
    thread,
    time::{Duration, Instant},
};
pub struct Output {
    pub stdout: Vec<u8>,
    pub stderr: String,
    pub status: ExitStatus,
}
struct Process {
    child: Child,
    stopped: bool,
    #[cfg(unix)]
    own_group: bool,
    #[cfg(windows)]
    job: Option<windows_job::ProcessJob>,
}
impl Process {
    fn spawn(command: &mut Command, own_group: bool) -> Result<Self, ApiError> {
        operation::check()?;
        command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000);
        }
        #[cfg(unix)]
        {
            use std::os::unix::process::CommandExt;
            if own_group {
                command.process_group(0);
            }
        }
        let child = command
            .spawn()
            .map_err(|e| failure(format!("无法启动受控工具：{e}")))?;
        let _ = own_group;
        let mut process = Self {
            child,
            stopped: false,
            #[cfg(unix)]
            own_group,
            #[cfg(windows)]
            job: None,
        };
        #[cfg(windows)]
        {
            process.job = Some(windows_job::ProcessJob::attach(&process.child).map_err(failure)?);
        }
        Ok(process)
    }
    fn stop(&mut self) {
        if self.stopped {
            return;
        }
        self.stopped = true;
        #[cfg(windows)]
        {
            self.job.take();
        }
        #[cfg(unix)]
        unsafe {
            if self.own_group {
                libc::kill(-(self.child.id() as i32), libc::SIGKILL);
            }
        }
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}
impl Drop for Process {
    fn drop(&mut self) {
        self.stop();
    }
}
fn failure(message: impl Into<String>) -> ApiError {
    ApiError {
        status: Some(503),
        message: message.into(),
        code: None,
        diagnostic: Some("controlled-process"),
    }
}
fn read(mut pipe: impl Read, limit: usize, strict: bool) -> std::io::Result<Vec<u8>> {
    let mut result = Vec::new();
    let mut buffer = [0_u8; 16384];
    loop {
        let length = pipe.read(&mut buffer)?;
        if length == 0 {
            return Ok(result);
        }
        let keep = length.min(limit.saturating_sub(result.len()));
        result.extend_from_slice(&buffer[..keep]);
        if strict && keep < length {
            return Err(std::io::Error::new(
                std::io::ErrorKind::InvalidData,
                "子进程输出超过容量上限。",
            ));
        }
    }
}
pub fn run(
    command: &mut Command,
    input: impl AsRef<[u8]>,
    output_limit: usize,
    timeout: Duration,
) -> Result<Output, ApiError> {
    run_reader(
        command,
        std::io::Cursor::new(input.as_ref()),
        output_limit,
        timeout,
    )
}

/// Stream a bounded, local input without creating a second in-memory frame.
/// Killing the process closes the pipe before scoped writers are joined.
pub fn run_reader(
    command: &mut Command,
    input: impl Read + Send,
    output_limit: usize,
    timeout: Duration,
) -> Result<Output, ApiError> {
    run_piped(command, input, timeout, move |stdout| {
        read(stdout, output_limit, true)
    })
}
/// Stream tool output into a caller-owned budgeted writer; errors terminate its process tree.
pub fn run_to_writer(
    command: &mut Command,
    mut output: impl std::io::Write + Send,
    timeout: Duration,
) -> Result<Output, ApiError> {
    run_piped(command, std::io::empty(), timeout, move |mut stdout| {
        std::io::copy(&mut stdout, &mut output)?;
        output.flush()?;
        Ok(Vec::new())
    })
}
fn run_piped(
    command: &mut Command,
    mut input: impl Read + Send,
    timeout: Duration,
    read_output: impl FnOnce(std::process::ChildStdout) -> std::io::Result<Vec<u8>> + Send,
) -> Result<Output, ApiError> {
    let mut process = Process::spawn(command, true)?;
    let mut stdin = process
        .child
        .stdin
        .take()
        .ok_or_else(|| failure("工具缺少输入管道。"))?;
    let stdout = process
        .child
        .stdout
        .take()
        .ok_or_else(|| failure("工具缺少输出管道。"))?;
    let stderr = process
        .child
        .stderr
        .take()
        .ok_or_else(|| failure("工具缺少诊断管道。"))?;
    thread::scope(move |threads| {
        // The process guard is dropped before scoped pipe threads are joined on unwind.
        let mut process = process;
        let writer = threads.spawn(move || std::io::copy(&mut input, &mut stdin));
        let (sender, receiver) = mpsc::channel();
        let reader = threads.spawn(move || {
            let _ = sender.send(read_output(stdout));
        });
        let diagnostics = threads.spawn(move || read(stderr, 8192, false));
        let deadline = Instant::now() + timeout;
        let mut output = None;
        let status = loop {
            if let Err(cause) = operation::check() {
                break Err(cause);
            }
            if output.is_none() {
                match receiver.try_recv() {
                    Ok(Ok(bytes)) => output = Some(bytes),
                    Ok(Err(cause)) => break Err(failure(cause.to_string())),
                    Err(mpsc::TryRecvError::Disconnected) => {
                        break Err(failure("工具输出线程中断。"));
                    }
                    Err(mpsc::TryRecvError::Empty) => {}
                }
            }
            match process.child.try_wait() {
                Ok(Some(status)) => break Ok(status),
                Ok(None) => {}
                Err(cause) => break Err(failure(cause.to_string())),
            }
            if Instant::now() >= deadline {
                break Err(ApiError {
                    status: Some(504),
                    message: "受控工具超过运行时限，已终止进程树。".into(),
                    code: None,
                    diagnostic: Some("controlled-process-timeout"),
                });
            }
            thread::sleep(Duration::from_millis(20));
        };
        process.stop();
        let write_result = writer.join().map_err(|_| failure("工具输入线程异常。"))?;
        reader.join().map_err(|_| failure("工具输出线程异常。"))?;
        let diagnostic = diagnostics
            .join()
            .map_err(|_| failure("工具诊断线程异常。"))?
            .map_err(|e| failure(e.to_string()))?;
        let status = status?;
        let bytes = match output {
            Some(bytes) => bytes,
            None => receiver
                .recv()
                .map_err(|_| failure("工具没有返回输出。"))?
                .map_err(|e| failure(e.to_string()))?,
        };
        if status.success() {
            write_result.map_err(|e| failure(e.to_string()))?;
        }
        Ok(Output {
            stdout: bytes,
            stderr: String::from_utf8_lossy(&diagnostic).into_owned(),
            status,
        })
    })
}
