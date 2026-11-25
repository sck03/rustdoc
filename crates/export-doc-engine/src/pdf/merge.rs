//! The merge process reads a length-delimited stream, never arbitrary paths.
use crate::engine::error::{Result, invalid, unavailable};
use std::{
    io::{Read, Write},
    path::Path,
    process::Command,
    time::Duration,
};
pub const MAX_MERGE_INPUT: usize = 128 * 1024 * 1024;
const MAX_OUTPUT: usize = 64 * 1024 * 1024;
pub(crate) fn write_input(
    inputs: impl ExactSizeIterator<Item = Vec<u8>>,
    output: &mut impl Write,
) -> Result<()> {
    if inputs.len() == 0 || inputs.len() > 100 {
        return Err(invalid("请选择 1–100 个 PDF 文件。"));
    }
    output.write_all(&(inputs.len() as u32).to_le_bytes())?;
    let mut total = 0usize;
    for bytes in inputs {
        crate::operation::check()?;
        total = total
            .checked_add(bytes.len())
            .filter(|n| *n <= MAX_MERGE_INPUT)
            .ok_or_else(|| invalid("待合并 PDF 总大小超过 128 MiB。"))?;
        if !bytes.starts_with(b"%PDF-") {
            return Err(invalid("请选择有效的 PDF 文件。"));
        }
        output.write_all(&(bytes.len() as u64).to_le_bytes())?;
        output.write_all(&bytes)?;
    }
    Ok(())
}
pub fn merge(input: impl Read + Send, library: &Path) -> Result<Vec<u8>> {
    crate::paths::ensure_safe_absolute(library).map_err(unavailable)?;
    if !library.is_file() {
        return Err(unavailable("未安装随包 PDFium，无法合并 PDF。"));
    }
    let mut command = Command::new(std::env::current_exe()?);
    command
        .arg("--pdf-merge-worker")
        .arg("--pdfium")
        .arg(library);
    let result = crate::controlled_process::run_reader(
        &mut command,
        input,
        MAX_OUTPUT,
        Duration::from_secs(120),
    )?;
    if !result.status.success() {
        return Err(if result.status.code() == Some(1) {
            invalid(result.stderr.trim())
        } else {
            unavailable("PDF 合并进程异常退出。")
        });
    }
    if !result.stdout.starts_with(b"%PDF-") {
        return Err(unavailable("PDF 合并进程未返回有效文件。"));
    }
    Ok(result.stdout)
}
pub(super) fn worker(args: &[String]) -> std::result::Result<(), String> {
    let library = args
        .iter()
        .position(|a| a == "--pdfium")
        .and_then(|i| args.get(i + 1))
        .ok_or("缺少 PDFium 路径。")?;
    let mut input = std::io::stdin();
    let files = read_input(&mut input)?;
    let bytes = super::native::merge(&files, Path::new(library), MAX_OUTPUT)?;
    std::io::stdout()
        .write_all(&bytes)
        .and_then(|_| std::io::stdout().flush())
        .map_err(|e| e.to_string())
}
fn read_input(input: &mut impl Read) -> std::result::Result<Vec<Vec<u8>>, String> {
    let mut count = [0u8; 4];
    input
        .read_exact(&mut count)
        .map_err(|_| "PDF 合并输入不完整。")?;
    let count = u32::from_le_bytes(count) as usize;
    if !(1..=100).contains(&count) {
        return Err("PDF 文件数量超限。".into());
    }
    let mut files = vec![];
    let mut total = 0usize;
    for _ in 0..count {
        let mut size = [0u8; 8];
        input
            .read_exact(&mut size)
            .map_err(|_| "PDF 合并输入不完整。")?;
        let size = usize::try_from(u64::from_le_bytes(size)).map_err(|_| "PDF 容量超限。")?;
        total = total
            .checked_add(size)
            .filter(|n| *n <= MAX_MERGE_INPUT)
            .ok_or("PDF 总容量超过 128 MiB。")?;
        let mut bytes = vec![0; size];
        input
            .read_exact(&mut bytes)
            .map_err(|_| "PDF 合并输入不完整。")?;
        files.push(bytes);
    }
    let mut trailing = [0; 1];
    if input.read(&mut trailing).map_err(|e| e.to_string())? != 0 {
        return Err("PDF 合并输入存在多余内容。".into());
    }
    Ok(files)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn streamed_frames_roundtrip_and_reject_truncation_trailing_data_and_limits() {
        let files = vec![b"%PDF-first".to_vec(), b"%PDF-second".to_vec()];
        let mut frame = Vec::new();
        write_input(files.clone().into_iter(), &mut frame).unwrap();
        assert_eq!(read_input(&mut frame.as_slice()).unwrap(), files);
        assert!(read_input(&mut &frame[..frame.len() - 1]).is_err());
        frame.push(0);
        assert!(read_input(&mut frame.as_slice()).is_err());
        assert!(write_input(Vec::new().into_iter(), &mut Vec::new()).is_err());
        assert!(write_input(vec![b"invalid".to_vec()].into_iter(), &mut Vec::new()).is_err());
        let mut oversized = 1u32.to_le_bytes().to_vec();
        oversized.extend_from_slice(&((MAX_MERGE_INPUT + 1) as u64).to_le_bytes());
        assert!(read_input(&mut oversized.as_slice()).is_err());
    }
    #[test]
    fn streaming_propagates_write_failures_and_cancellation() {
        struct Failed;
        impl Write for Failed {
            fn write(&mut self, _: &[u8]) -> std::io::Result<usize> {
                Err(std::io::Error::other("disk full"))
            }
            fn flush(&mut self) -> std::io::Result<()> {
                Ok(())
            }
        }
        assert!(write_input(vec![b"%PDF-file".to_vec()].into_iter(), &mut Failed).is_err());
        let scope = crate::operation::OperationScope::new(Duration::from_secs(5));
        scope.cancel();
        assert_eq!(
            scope
                .run(|| write_input(vec![b"%PDF-file".to_vec()].into_iter(), &mut Vec::new()))
                .unwrap_err()
                .status,
            Some(504)
        );
    }
}
