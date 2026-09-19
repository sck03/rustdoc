//! A bounded native process tree with cancellable pipes. No shell interpolation.
use crate::{api::ApiError, operation};
use std::{
    io::{Read, Write},
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
    #[cfg(windows)]
    job: Option<crate::runtime::ProcessJob>,
}
impl Process {
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
            libc::kill(-(self.child.id() as i32), libc::SIGKILL);
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
    input: Vec<u8>,
    output_limit: usize,
    timeout: Duration,
) -> Result<Output, ApiError> {
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
        command.process_group(0);
    }
    let child = command
        .spawn()
        .map_err(|e| failure(format!("无法启动受控工具：{e}")))?;
    let mut process = Process {
        child,
        stopped: false,
        #[cfg(windows)]
        job: None,
    };
    #[cfg(windows)]
    {
        process.job = Some(crate::runtime::ProcessJob::attach(&process.child).map_err(failure)?);
    }
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
    let writer = thread::spawn(move || stdin.write_all(&input));
    let (sender, receiver) = mpsc::channel();
    let reader = thread::spawn(move || {
        let _ = sender.send(read(stdout, output_limit, true));
    });
    let diagnostics = thread::spawn(move || read(stderr, 8192, false));
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
                Err(mpsc::TryRecvError::Disconnected) => break Err(failure("工具输出线程中断。")),
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
}
