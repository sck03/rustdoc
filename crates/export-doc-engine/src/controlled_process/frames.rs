//! One bounded request/response at a time; the process and pipes live for one task.
use super::*;
use std::io::Write;
use std::sync::mpsc::{Receiver, SyncSender};

pub(crate) struct FramedProcess {
    process: Process,
    input: Option<SyncSender<Vec<u8>>>,
    output: Option<Receiver<std::io::Result<Vec<u8>>>>,
    threads: Vec<thread::JoinHandle<()>>,
    deadline: Instant,
}

impl FramedProcess {
    pub fn start(
        command: &mut Command,
        output_limit: usize,
        timeout: Duration,
    ) -> Result<Self, ApiError> {
        let mut process = Process::spawn(command, false)?;
        let mut stdin = process
            .child
            .stdin
            .take()
            .ok_or_else(|| failure("工具缺少输入管道。"))?;
        let mut stdout = process
            .child
            .stdout
            .take()
            .ok_or_else(|| failure("工具缺少输出管道。"))?;
        let stderr = process
            .child
            .stderr
            .take()
            .ok_or_else(|| failure("工具缺少诊断管道。"))?;
        let (input, requests) = mpsc::sync_channel::<Vec<u8>>(1);
        let (responses, output) = mpsc::sync_channel(1);
        let writer = thread::spawn(move || {
            while let Ok(bytes) = requests.recv() {
                let Ok(length) = u32::try_from(bytes.len()) else {
                    break;
                };
                if stdin
                    .write_all(&length.to_le_bytes())
                    .and_then(|_| stdin.write_all(&bytes))
                    .and_then(|_| stdin.flush())
                    .is_err()
                {
                    break;
                }
                if length == 0 {
                    break;
                }
            }
        });
        let reader = thread::spawn(move || {
            loop {
                let frame = (|| {
                    let mut header = [0; 4];
                    stdout.read_exact(&mut header)?;
                    let length = u32::from_le_bytes(header) as usize;
                    if length == 0 || length > output_limit {
                        return Err(std::io::Error::new(
                            std::io::ErrorKind::InvalidData,
                            "工具响应帧超出容量范围。",
                        ));
                    }
                    let mut bytes = vec![0; length];
                    stdout.read_exact(&mut bytes)?;
                    Ok(bytes)
                })();
                let failed = frame.is_err();
                if responses.send(frame).is_err() || failed {
                    break;
                }
            }
        });
        let diagnostics = thread::spawn(move || {
            let _ = read(stderr, 8192, false);
        });
        Ok(Self {
            process,
            input: Some(input),
            output: Some(output),
            threads: vec![writer, reader, diagnostics],
            deadline: Instant::now() + timeout,
        })
    }

    pub fn exchange(&mut self, input: Vec<u8>) -> Result<Vec<u8>, ApiError> {
        operation::check()?;
        if input.is_empty() || input.len() > 25 * 1024 * 1024 {
            return Err(failure("工具输入帧超出容量范围。"));
        }
        self.input
            .as_ref()
            .ok_or_else(|| failure("工具已关闭。"))?
            .try_send(input)
            .map_err(|_| failure("工具输入管道不可用。"))?;
        let deadline = self.deadline.min(Instant::now() + Duration::from_secs(90));
        loop {
            operation::check()?;
            match self
                .output
                .as_ref()
                .ok_or_else(|| failure("工具已关闭。"))?
                .try_recv()
            {
                Ok(result) => return result.map_err(|e| failure(e.to_string())),
                Err(mpsc::TryRecvError::Disconnected) => return Err(failure("工具输出管道中断。")),
                Err(mpsc::TryRecvError::Empty) => {}
            }
            if Instant::now() >= deadline {
                return Err(ApiError {
                    status: Some(504),
                    message: "OCR 超过运行时限。".into(),
                    code: None,
                    diagnostic: Some("controlled-process-timeout"),
                    ..Default::default()
                });
            }
            thread::sleep(Duration::from_millis(20));
        }
    }
}

impl Drop for FramedProcess {
    fn drop(&mut self) {
        // Kill before joining blocked pipe readers; close channels before joining senders.
        self.process.stop();
        self.input.take();
        self.output.take();
        for worker in self.threads.drain(..) {
            let _ = worker.join();
        }
    }
}
