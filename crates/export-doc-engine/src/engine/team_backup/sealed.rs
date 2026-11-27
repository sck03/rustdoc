//! Authenticated repository key wrapping and shared managed scratch helpers.
use super::error::{Result, invalid, unavailable};
#[cfg(test)]
use crate::engine::backup_io::copy_and_hash;
use aes_gcm::{
    Aes256Gcm, Nonce, Tag,
    aead::{AeadInOut, KeyInit},
};
use pbkdf2::pbkdf2_hmac_array;
use sha2::Sha256;
use std::{
    io::{Read, Write},
    path::{Path, PathBuf},
};

const PBKDF2_ITERATIONS: u32 = 200_000;
const SALT_LENGTH: usize = 32;
pub(super) mod object;
mod stream;
pub(super) use stream::SealingWriter;
pub(super) use stream::open as open_to;
#[cfg(test)]
pub(super) use stream::seal as seal_to;
pub(super) const MAX_FILES: usize = 4096;

/// 原子写入（保持目标文件完整，失败时清理临时文件）。
pub(super) fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    crate::paths::atomic_write(path, bytes)
        .map_err(|cause| unavailable(format!("写入包文件失败：{cause}")))
}
/// 生成临时工作目录（随调用方清理）。
pub(super) fn working_directory(parent: &Path) -> Result<PathBuf> {
    super::scratch::create(parent, ".work-")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bounded_streams_check_actual_bytes_and_digest() {
        let content = vec![17; 128 * 1024 + 3];
        let mut copied = Vec::new();
        let (size, hash) =
            copy_and_hash(&mut content.as_slice(), &mut copied, content.len() as u64).unwrap();
        assert_eq!(size, content.len() as u64);
        assert_eq!(copied, content);
        assert_eq!(hash, super::super::sha256_hex(&content));
        let mut copied = Vec::new();
        let error = copy_and_hash(
            &mut content.as_slice(),
            &mut copied,
            content.len() as u64 - 1,
        )
        .unwrap_err();
        assert_eq!(error.status, Some(400));
        assert!(copied.len() < content.len());
    }
}
