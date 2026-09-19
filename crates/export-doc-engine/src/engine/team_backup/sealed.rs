//! 密封包格式：zip 负载 + AES-256-GCM 密封，密钥由包密码经 PBKDF2-HMAC-SHA256 派生。
//! 灾备包与服务器迁移包共用同一格式，仅幻数与清单结构不同。
use super::{
    error::{Result, invalid, unavailable},
    sha256_hex,
};
use aes_gcm::{
    Aes256Gcm, Nonce,
    aead::{Aead, KeyInit, Payload},
};
use pbkdf2::pbkdf2_hmac_array;
use sha2::Sha256;
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
};
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};

const PBKDF2_ITERATIONS: u32 = 200_000;
const SALT_LENGTH: usize = 32;
const NONCE_LENGTH: usize = 12;
const MAX_PLAINTEXT_BYTES: usize = 8 * 1024 * 1024 * 1024;

/// 一个待密封的文件条目：磁盘路径 + 包内相对路径。
pub(super) struct Entry<'a> {
    pub source: &'a Path,
    pub name: &'a str,
}

/// 生成 zip 负载。条目顺序与清单顺序一致，调用方负责写清单。
pub(super) fn zip_payload(entries: &[Entry<'_>]) -> Result<Vec<u8>> {
    let mut writer = ZipWriter::new(std::io::Cursor::new(Vec::new()));
    for entry in entries {
        let bytes = fs::read(entry.source)?;
        if bytes.len() > MAX_PLAINTEXT_BYTES {
            return Err(invalid("包内容超过 8 GiB 明文上限。"));
        }
        writer
            .start_file(
                entry.name,
                SimpleFileOptions::default().compression_method(CompressionMethod::Deflated),
            )
            .map_err(|cause| unavailable(cause.to_string()))?;
        writer
            .write_all(&bytes)
            .map_err(|cause| unavailable(cause.to_string()))?;
    }
    let cursor = writer
        .finish()
        .map_err(|cause| unavailable(cause.to_string()))?;
    Ok(cursor.into_inner())
}

/// 派生密钥并密封负载。返回magic || salt || nonce || ciphertext。
pub(super) fn seal(magic: &[u8], password: &str, payload: &[u8]) -> Result<Vec<u8>> {
    if payload.len() > MAX_PLAINTEXT_BYTES {
        return Err(invalid("包内容超过 8 GiB 明文上限。"));
    }
    let mut salt = vec![0u8; SALT_LENGTH];
    getrandom::fill(&mut salt).map_err(|_| unavailable("无法生成包随机盐。"))?;
    let key = pbkdf2_hmac_array::<Sha256, 32>(password.as_bytes(), &salt, PBKDF2_ITERATIONS);
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|_| unavailable("包密钥长度无效。"))?;
    let mut nonce = [0u8; NONCE_LENGTH];
    getrandom::fill(&mut nonce).map_err(|_| unavailable("无法生成包随机数。"))?;
    let encrypted = cipher
        .encrypt(
            &Nonce::from(nonce),
            Payload {
                msg: payload,
                aad: magic,
            },
        )
        .map_err(|_| unavailable("密封加密失败。"))?;
    let mut output = Vec::with_capacity(magic.len() + salt.len() + nonce.len() + encrypted.len());
    output.extend_from_slice(magic);
    output.extend_from_slice(&salt);
    output.extend_from_slice(&nonce);
    output.extend_from_slice(&encrypted);
    Ok(output)
}

/// 打开密封包。校验幻数、大小上限与认证标签。
pub(super) fn open(magic: &[u8], password: &str, sealed: &[u8]) -> Result<Vec<u8>> {
    if sealed.len() < magic.len() + SALT_LENGTH + NONCE_LENGTH + 16 {
        return Err(invalid("包文件不完整或格式无效。"));
    }
    if &sealed[..magic.len()] != magic {
        return Err(invalid("包幻数不匹配，不是当前类型的密封包。"));
    }
    if sealed.len() > MAX_PLAINTEXT_BYTES + 1024 * 1024 {
        return Err(invalid("包文件超过容量上限。"));
    }
    let salt = &sealed[magic.len()..magic.len() + SALT_LENGTH];
    let nonce: [u8; NONCE_LENGTH] = sealed
        [magic.len() + SALT_LENGTH..magic.len() + SALT_LENGTH + NONCE_LENGTH]
        .try_into()
        .expect("validated nonce length");
    let ciphertext = &sealed[magic.len() + SALT_LENGTH + NONCE_LENGTH..];
    let key = pbkdf2_hmac_array::<Sha256, 32>(password.as_bytes(), salt, PBKDF2_ITERATIONS);
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|_| unavailable("包密钥长度无效。"))?;
    cipher
        .decrypt(
            &Nonce::from(nonce),
            Payload {
                msg: ciphertext,
                aad: magic,
            },
        )
        .map_err(|_| invalid("包密码错误、包已损坏或本机密钥状态不匹配。"))
}

/// 把字节数组解包到目标目录，返回解出的文件名清单。解包前必须已通过密封校验。
pub(super) fn unpack(payload: &[u8], destination: &Path) -> Result<Vec<(String, Vec<u8>, String)>> {
    crate::paths::ensure_safe_absolute(destination).map_err(invalid)?;
    fs::create_dir_all(destination)?;
    let mut archive = zip::ZipArchive::new(std::io::Cursor::new(payload))
        .map_err(|cause| invalid(format!("包内容不是有效 zip：{cause}")))?;
    let mut entries = Vec::new();
    for index in 0..archive.len() {
        let mut file = archive
            .by_index(index)
            .map_err(|cause| unavailable(cause.to_string()))?;
        // 统一分隔符后再校验：Windows 上 zip 会把条目名中的 / 转成 \，
        // 直接按原始名字符判断会把合法的嵌套条目误判为非法路径。
        let name = file.name().replace('\\', "/");
        if name.is_empty() || name.contains("..") || name.starts_with('/') {
            return Err(invalid("包内含非法路径条目。"));
        }
        let mut bytes = Vec::new();
        std::io::copy(&mut file, &mut bytes).map_err(|cause| unavailable(cause.to_string()))?;
        if bytes.len() > MAX_PLAINTEXT_BYTES {
            return Err(invalid("包内条目超过容量上限。"));
        }
        let digest = sha256_hex(&bytes);
        let path = destination.join(&name);
        crate::paths::ensure_safe_absolute(&path).map_err(invalid)?;
        // 拒绝绝对路径替换（例如 Windows 盘符条目）造成的目录逃逸。
        if !path.starts_with(destination) {
            return Err(invalid("包内含非法路径条目。"));
        }
        if path.parent().is_some() {
            fs::create_dir_all(path.parent().expect("validated parent"))?;
        }
        atomic_write(&path, &bytes)?;
        entries.push((name, bytes, digest));
    }
    Ok(entries)
}

/// 清单中记录的文件摘要。
pub(super) struct ManifestFile {
    pub name: String,
    pub size_bytes: u64,
    pub sha256: String,
}
impl ManifestFile {
    pub(super) fn from_path(name: &str, path: &Path) -> Result<Self> {
        let bytes = fs::read(path)?;
        Ok(Self {
            name: name.into(),
            size_bytes: bytes.len() as u64,
            sha256: sha256_hex(&bytes),
        })
    }
    pub(super) fn to_json(&self) -> serde_json::Value {
        serde_json::json!({"relativePath":self.name,"sizeBytes":self.size_bytes,"sha256":self.sha256})
    }
}
/// 校验解包结果与清单一致（名称、大小、摘要）。
pub(super) fn verify_manifest(
    entries: &[(String, Vec<u8>, String)],
    files: &[ManifestFile],
) -> Result<()> {
    if entries.len() != files.len() {
        return Err(invalid("包内文件数与清单不一致。"));
    }
    for (index, file) in files.iter().enumerate() {
        let (name, bytes, digest) = &entries[index];
        if name != &file.name || *digest != file.sha256 || bytes.len() as u64 != file.size_bytes {
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
