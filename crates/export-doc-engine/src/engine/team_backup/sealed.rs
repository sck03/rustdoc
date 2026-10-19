//! 密封包格式：zip 负载 + AES-256-GCM 密封，密钥由包密码经 PBKDF2-HMAC-SHA256 派生。
//! 灾备包与服务器迁移包共用同一格式，仅幻数与清单结构不同。
use super::error::{Result, invalid, unavailable};
use crate::engine::backup_io::{copy_and_hash, copy_chunks};
use aes_gcm::{
    Aes256Gcm, Nonce, Tag,
    aead::{AeadInOut, KeyInit},
};
use pbkdf2::pbkdf2_hmac_array;
use sha2::Sha256;
use std::{
    fs,
    io::{Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
};
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};

const PBKDF2_ITERATIONS: u32 = 200_000;
const SALT_LENGTH: usize = 32;
const NONCE_LENGTH: usize = 12;
pub(super) const MAX_EXPANDED_BYTES: u64 = crate::engine::backup_io::MAX_FILE_BYTES;
pub(super) const MAX_PACKAGE_BYTES: u64 = MAX_EXPANDED_BYTES;
const LEGACY_MAX_BYTES: usize = 256 * 1024 * 1024;
#[cfg(test)]
const MAX_ARCHIVE_BYTES: usize = LEGACY_MAX_BYTES - 128;
mod stream;
pub(super) use stream::{open as open_to, seal as seal_to};
pub(super) const MAX_FILES: usize = 4096;

/// 一个待密封的文件条目：磁盘路径 + 包内相对路径。
pub(super) struct Entry<'a> {
    pub source: &'a Path,
    pub name: &'a str,
}

/// 生成 zip 负载。条目顺序与清单顺序一致，调用方负责写清单。
pub(super) fn zip_payload(entries: &[Entry<'_>], output: impl Write + Seek) -> Result<()> {
    let mut writer = ZipWriter::new(output);
    let mut total = 0_u64;
    for entry in entries {
        crate::operation::check()?;
        crate::paths::ensure_safe_absolute(entry.source).map_err(invalid)?;
        let mut input = fs::File::open(entry.source)?;
        let remaining = MAX_EXPANDED_BYTES as u64 - total;
        if input.metadata()?.len() > remaining {
            return Err(invalid("包展开内容超过可表示的文件大小。"));
        }
        writer
            .start_file(
                entry.name,
                SimpleFileOptions::default()
                    .large_file(true)
                    .compression_method(CompressionMethod::Deflated),
            )
            .map_err(|cause| unavailable(cause.to_string()))?;
        total += copy_chunks(&mut input, &mut writer, remaining, |_| {})?;
    }
    let mut output = writer
        .finish()
        .map_err(|cause| unavailable(cause.to_string()))?;
    if output.seek(SeekFrom::End(0))? > MAX_PACKAGE_BYTES {
        return Err(invalid("压缩后的迁移/灾备包超过可表示的文件大小。"));
    }
    output.flush()?;
    Ok(())
}

/// 派生密钥并密封负载。返回magic || salt || nonce || ciphertext。
#[cfg(test)]
pub(super) fn seal(
    magic: &[u8],
    password: &str,
    payload: Vec<u8>,
    output: &mut impl Write,
) -> Result<()> {
    let mut payload = zeroize::Zeroizing::new(payload);
    if magic.len() > 64 || payload.len() > MAX_ARCHIVE_BYTES {
        return Err(invalid("压缩后的迁移/灾备包超过 256 MiB 传输上限。"));
    }
    let mut salt = vec![0u8; SALT_LENGTH];
    getrandom::fill(&mut salt).map_err(|_| unavailable("无法生成包随机盐。"))?;
    let key = zeroize::Zeroizing::new(pbkdf2_hmac_array::<Sha256, 32>(
        password.as_bytes(),
        &salt,
        PBKDF2_ITERATIONS,
    ));
    let cipher =
        Aes256Gcm::new_from_slice(key.as_ref()).map_err(|_| unavailable("包密钥长度无效。"))?;
    let mut nonce = [0u8; NONCE_LENGTH];
    getrandom::fill(&mut nonce).map_err(|_| unavailable("无法生成包随机数。"))?;
    crate::operation::check()?;
    let tag = cipher
        .encrypt_inout_detached(&Nonce::from(nonce), magic, payload.as_mut_slice().into())
        .map_err(|_| unavailable("密封加密失败。"))?;
    crate::operation::check()?;
    output.write_all(magic)?;
    output.write_all(&salt)?;
    output.write_all(&nonce)?;
    output.write_all(&payload)?;
    output.write_all(&tag)?;
    Ok(())
}

/// 打开密封包。校验幻数、大小上限与认证标签。
pub(super) fn open(
    magic: &[u8],
    password: &str,
    mut input: impl Read,
) -> Result<zeroize::Zeroizing<Vec<u8>>> {
    if magic.len() > 64 {
        return Err(invalid("包格式标记无效。"));
    }
    let mut header = vec![0_u8; magic.len() + SALT_LENGTH + NONCE_LENGTH];
    input
        .read_exact(&mut header)
        .map_err(|_| invalid("包文件不完整或格式无效。"))?;
    if &header[..magic.len()] != magic {
        return Err(invalid("包幻数不匹配，不是当前类型的密封包。"));
    }
    let mut payload = zeroize::Zeroizing::new(Vec::new());
    // The original format allowed a 256 MiB ZIP plus its authentication tag.
    copy_chunks(
        &mut input,
        &mut *payload,
        LEGACY_MAX_BYTES as u64 + 16,
        |_| {},
    )?;
    if payload.len() < 16 {
        return Err(invalid("包文件不完整或格式无效。"));
    }
    if payload.len() > LEGACY_MAX_BYTES + 16 {
        return Err(invalid("包文件超过容量上限。"));
    }
    let salt = &header[magic.len()..magic.len() + SALT_LENGTH];
    let nonce: [u8; NONCE_LENGTH] = header
        [magic.len() + SALT_LENGTH..magic.len() + SALT_LENGTH + NONCE_LENGTH]
        .try_into()
        .expect("validated nonce length");
    let tag_bytes: [u8; 16] = payload[payload.len() - 16..]
        .try_into()
        .expect("validated tag length");
    let tag = Tag::from(tag_bytes);
    let size = payload.len() - 16;
    payload.truncate(size);
    let key = zeroize::Zeroizing::new(pbkdf2_hmac_array::<Sha256, 32>(
        password.as_bytes(),
        salt,
        PBKDF2_ITERATIONS,
    ));
    let cipher =
        Aes256Gcm::new_from_slice(key.as_ref()).map_err(|_| unavailable("包密钥长度无效。"))?;
    crate::operation::check()?;
    cipher
        .decrypt_inout_detached(
            &Nonce::from(nonce),
            magic,
            payload.as_mut_slice().into(),
            &tag,
        )
        .map_err(|_| invalid("包密码错误、包已损坏或本机密钥状态不匹配。"))?;
    crate::operation::check()?;
    Ok(payload)
}

/// 把字节数组解包到目标目录，返回解出的文件名清单。解包前必须已通过密封校验。
pub(super) fn unpack(payload: impl Read + Seek, destination: &Path) -> Result<Vec<ManifestFile>> {
    crate::paths::ensure_safe_absolute(destination).map_err(invalid)?;
    fs::create_dir_all(destination)?;
    let mut archive = zip::ZipArchive::new(payload)
        .map_err(|cause| invalid(format!("包内容不是有效 zip：{cause}")))?;
    let mut entries = Vec::new();
    if archive.len() > MAX_FILES {
        return Err(invalid("包内文件数量超过上限。"));
    }
    let expanded = (0..archive.len()).try_fold(0_u64, |total, index| {
        let entry = archive
            .by_index(index)
            .map_err(|cause| invalid(cause.to_string()))?;
        total
            .checked_add(entry.size())
            .ok_or_else(|| invalid("包展开大小超出可表示范围。"))
    })?;
    if expanded > fs2::available_space(destination)? {
        return Err(unavailable(
            "恢复暂存目录剩余空间不足以展开此备份，请释放空间或改用容量足够的受管数据目录。",
        ));
    }
    let mut names = std::collections::BTreeSet::new();
    let mut total = 0_u64;
    for index in 0..archive.len() {
        crate::operation::check()?;
        let mut file = archive
            .by_index(index)
            .map_err(|cause| unavailable(cause.to_string()))?;
        // Portable package paths use forward slashes and reject ambiguous names.
        let name = file
            .name()
            .map_err(|cause| invalid(cause.to_string()))?
            .into_owned();
        if name
            .split('/')
            .any(|part| !crate::paths::valid_file_name(part))
            || name.contains('\\')
            || !names.insert(if cfg!(windows) {
                name.to_lowercase()
            } else {
                name.clone()
            })
            || file
                .unix_mode()
                .is_some_and(|mode| mode & 0o170000 == 0o120000)
        {
            return Err(invalid("包内含非法路径条目。"));
        }
        let remaining = MAX_EXPANDED_BYTES as u64 - total;
        if file.size() > remaining {
            return Err(invalid("包解压容量超过上限。"));
        }
        let path = destination.join(&name);
        crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
        // 拒绝绝对路径替换（例如 Windows 盘符条目）造成的目录逃逸。
        if !path.starts_with(destination) {
            return Err(invalid("包内含非法路径条目。"));
        }
        if path.parent().is_some() {
            fs::create_dir_all(path.parent().expect("validated parent"))?;
        }
        // The caller owns a private staging directory. Only its validated marker
        // publishes the package; no unpacked payload needs to remain in memory.
        let mut output = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&path)?;
        let (size_bytes, sha256) = copy_and_hash(&mut file, &mut output, remaining)?;
        output.sync_all()?;
        total += size_bytes;
        entries.push(ManifestFile {
            name,
            size_bytes,
            sha256,
        });
    }
    Ok(entries)
}

/// 清单中记录的文件摘要。
#[derive(Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(super) struct ManifestFile {
    #[serde(rename = "relativePath")]
    pub name: String,
    pub size_bytes: u64,
    pub sha256: String,
}
impl ManifestFile {
    pub(super) fn from_path(name: &str, path: &Path) -> Result<Self> {
        crate::paths::ensure_safe_absolute(path).map_err(invalid)?;
        let mut input = fs::File::open(path)?;
        let metadata = input.metadata()?;
        if !metadata.is_file() || metadata.len() > MAX_EXPANDED_BYTES as u64 {
            return Err(invalid("包展开内容超过可表示的文件大小。"));
        }
        let (size_bytes, sha256) =
            copy_and_hash(&mut input, &mut std::io::sink(), MAX_EXPANDED_BYTES as u64)?;
        Ok(Self {
            name: name.into(),
            size_bytes,
            sha256,
        })
    }
}
/// 校验解包结果与清单一致（名称、大小、摘要）。
pub(super) fn verify_manifest(entries: &[ManifestFile], files: &[ManifestFile]) -> Result<()> {
    let mut entries: std::collections::BTreeMap<_, _> = entries
        .iter()
        .filter(|entry| entry.name != "manifest.json")
        .map(|entry| (entry.name.as_str(), entry))
        .collect();
    if entries.len() != files.len() {
        return Err(invalid("包内文件数与清单不一致。"));
    }
    for file in files {
        let entry = entries
            .remove(file.name.as_str())
            .ok_or_else(|| invalid("清单引用的文件缺失或重复。"))?;
        if entry.sha256 != file.sha256 || entry.size_bytes != file.size_bytes {
            return Err(invalid(format!(
                "包内文件 {} 与清单校验不一致。",
                file.name
            )));
        }
    }
    Ok(())
}

/// 原子写入（保持目标文件完整，失败时清理临时文件）。
pub(super) fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    crate::paths::atomic_write(path, bytes)
        .map_err(|cause| unavailable(format!("写入包文件失败：{cause}")))
}
/// 生成临时工作目录（随调用方清理）。
pub(super) fn working_directory(parent: &Path) -> Result<PathBuf> {
    crate::paths::ensure_safe_absolute(parent).map_err(invalid)?;
    let directory = parent.join(format!(
        ".work-{}",
        crate::paths::nonce().map_err(|cause| unavailable(cause))?
    ));
    fs::create_dir_all(&directory)?;
    crate::paths::ensure_safe_absolute(&directory).map_err(invalid)?;
    Ok(directory)
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
