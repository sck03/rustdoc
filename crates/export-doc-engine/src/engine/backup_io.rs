//! Bounded file transport shared by backup jobs and HTTP. Paths stay in the application boundary.
use super::{
    error::{Result, error, invalid, unavailable},
    tasks::FileOutput,
};
use sha2::{Digest, Sha256};
mod buffered;
pub use buffered::{Budget, Reservation};
use std::{
    fs,
    io::{Read, Seek, SeekFrom, Write},
    path::Path,
};

// Streaming transports have no application quota; checked u64 counters still reject overflow.
pub const MAX_FILE_BYTES: u64 = u64::MAX;
pub const TIMEOUT_SECONDS: u64 = export_doc_contracts::request_limits::TRANSFER_SECONDS;

pub enum Download {
    Memory {
        output: FileOutput,
        reservation: Reservation,
    },
    File {
        file_name: String,
        media_type: String,
        file: fs::File,
        size: u64,
    },
}

/// Owns the authorized file handle while verification runs outside publication locks.
pub(crate) struct PendingDownload {
    download: Download,
    expected: Option<(u64, String)>,
}
impl PendingDownload {
    pub(crate) fn ready(download: Download) -> Self {
        Self {
            download,
            expected: None,
        }
    }
    pub(crate) fn verify(mut self) -> Result<Download> {
        crate::operation::check()?;
        if let Some(expected) = self.expected {
            let Download::File { file, .. } = &mut self.download else {
                return Err(unavailable("备份校验状态无效。"));
            };
            if copy_and_hash(file, &mut std::io::sink(), MAX_FILE_BYTES)? != expected {
                return Err(unavailable("备份文件已变化，不能下载不完整结果。"));
            }
            file.seek(SeekFrom::Start(0))?;
        }
        crate::operation::check()?;
        Ok(self.download)
    }
}
impl Download {
    pub fn open(path: &Path, expected: Option<(u64, &str)>) -> Result<Self> {
        Self::prepare(path, expected)?.verify()
    }
    pub(crate) fn prepare(path: &Path, expected: Option<(u64, &str)>) -> Result<PendingDownload> {
        crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
        let file = fs::File::open(path).map_err(|cause| {
            if cause.kind() == std::io::ErrorKind::NotFound {
                error(404, "备份文件不存在或已按保留策略清理。")
            } else {
                cause.into()
            }
        })?;
        let metadata = file.metadata()?;
        if !metadata.is_file() || metadata.len() == 0 {
            return Err(unavailable(
                "已登记备份文件为空或类型无效，请检查备份目录。",
            ));
        }
        if expected.is_some_and(|(size, _)| metadata.len() != size) {
            return Err(unavailable("备份文件已变化，不能下载不完整结果。"));
        }
        Ok(PendingDownload {
            download: Self::File {
                file_name: path
                    .file_name()
                    .ok_or_else(|| invalid("备份文件名无效。"))?
                    .to_string_lossy()
                    .into_owned(),
                media_type: "application/octet-stream".into(),
                file,
                size: metadata.len(),
            },
            expected: expected.map(|(size, hash)| (size, hash.to_owned())),
        })
    }
    /// In-process callers have a bounded byte API; HTTP uses the file variant directly.
    pub fn into_memory(self) -> Result<FileOutput> {
        match self {
            Self::Memory { output, .. } => Ok(output),
            Self::File {
                file_name,
                media_type,
                mut file,
                size,
            } => {
                if size > 256 * 1024 * 1024 {
                    return Err(invalid("大备份请通过流式下载接口保存。"));
                }
                let mut content = Vec::with_capacity(size as usize);
                let (actual, _) = copy_and_hash(&mut file, &mut content, size)?;
                if actual != size {
                    return Err(unavailable("备份文件不完整。"));
                }
                Ok(FileOutput {
                    file_name,
                    media_type,
                    content,
                })
            }
        }
    }
}

pub(crate) fn copy_and_hash(
    input: &mut impl Read,
    output: &mut impl Write,
    maximum: u64,
) -> Result<(u64, String)> {
    let mut digest = Sha256::new();
    let size = copy_chunks(input, output, maximum, |bytes| digest.update(bytes))?;
    Ok((size, super::team_backup::hex(&digest.finalize())))
}
pub(crate) fn copy_chunks(
    input: &mut impl Read,
    output: &mut impl Write,
    maximum: u64,
    mut observe: impl FnMut(&[u8]),
) -> Result<u64> {
    let mut buffer = [0; 64 * 1024];
    let mut size = 0_u64;
    loop {
        crate::operation::check()?;
        let available = (maximum - size).saturating_add(1).min(buffer.len() as u64) as usize;
        let count = input.read(&mut buffer[..available])?;
        if count == 0 {
            return Ok(size);
        }
        if count as u64 > maximum - size {
            return Err(invalid("备份文件超过容量上限。"));
        }
        output.write_all(&buffer[..count])?;
        observe(&buffer[..count]);
        size += count as u64;
    }
}
pub(crate) fn digest_file(path: &Path) -> Result<(u64, String)> {
    crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
    copy_and_hash(
        &mut fs::File::open(path)?,
        &mut std::io::sink(),
        MAX_FILE_BYTES,
    )
}
