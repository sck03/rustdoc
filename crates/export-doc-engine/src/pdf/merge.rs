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
pub fn merge(inputs: &[Vec<u8>], library: &Path) -> Result<Vec<u8>> {
    if inputs.is_empty() || inputs.len() > 100 {
        return Err(invalid("请选择 1–100 个 PDF 文件。"));
    }
    let total = inputs
        .iter()
        .map(Vec::len)
        .try_fold(0usize, usize::checked_add)
        .ok_or_else(|| invalid("PDF 总容量超限。"))?;
    if total > MAX_MERGE_INPUT {
        return Err(invalid("待合并 PDF 总大小超过 128 MiB。"));
    }
    crate::paths::ensure_safe_absolute(library).map_err(unavailable)?;
    if !library.is_file() {
        return Err(unavailable("未安装随包 PDFium，无法合并 PDF。"));
    }
    let mut data = Vec::with_capacity(total + 4 + 8 * inputs.len());
    data.extend_from_slice(&(inputs.len() as u32).to_le_bytes());
    for bytes in inputs {
        if !bytes.starts_with(b"%PDF-") {
            return Err(invalid("请选择有效的 PDF 文件。"));
        }
        data.extend_from_slice(&(bytes.len() as u64).to_le_bytes());
        data.extend_from_slice(bytes);
    }
    let mut command = Command::new(std::env::current_exe()?);
    command
        .arg("--pdf-merge-worker")
        .arg("--pdfium")
        .arg(library);
    let result =
        crate::controlled_process::run(&mut command, data, MAX_OUTPUT, Duration::from_secs(120))?;
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
    let bytes = super::native::merge(&files, Path::new(library), MAX_OUTPUT)?;
    std::io::stdout()
        .write_all(&bytes)
        .and_then(|_| std::io::stdout().flush())
        .map_err(|e| e.to_string())
}
