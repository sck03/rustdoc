//! Installation-local credentials. The DB contains only authenticated ciphertext.
//! Key material stays under the injected runtime root or in deployment secrets.
mod private_files;
use aes_gcm::{
    Aes256Gcm, Nonce,
    aead::{Aead, KeyInit, Payload},
};
use base64::{Engine, prelude::BASE64_STANDARD};
pub(crate) use private_files::directory as private_directory;
use std::{
    fs::{File, OpenOptions},
    io::{Read, Write},
    path::{Path, PathBuf},
    sync::Mutex,
};
use zeroize::Zeroizing;

const PREFIX: &str = "edm-rust-aes256gcm-v1:";
pub const FIELDS: &[(&str, &str)] = &[
    ("/email/password", "emailPasswordSet"),
    ("/webDav/password", "webDavPasswordSet"),
    ("/system/postgreSqlPassword", "postgreSqlPasswordSet"),
    ("/ai/apiKey", "aiApiKeySet"),
];
pub struct Protector {
    root: PathBuf,
    environment_key: Option<Zeroizing<String>>,
    key: Mutex<Option<Zeroizing<[u8; 32]>>>,
}
impl Clone for Protector {
    fn clone(&self) -> Self {
        Self {
            root: self.root.clone(),
            environment_key: self.environment_key.clone(),
            key: Mutex::new(self.key.lock().ok().and_then(|key| key.clone())),
        }
    }
}
impl Protector {
    pub fn new(data_root: &Path) -> Self {
        Self {
            root: data_root.join("Security"),
            environment_key: std::env::var("EXPORTDOCMANAGER_MASTER_KEY")
                .ok()
                .map(Zeroizing::new),
            key: Mutex::new(None),
        }
    }
    fn with_key<T>(
        &self,
        create: bool,
        use_key: impl FnOnce(&[u8; 32]) -> Result<T, String>,
    ) -> Result<T, String> {
        let mut key = self.key.lock().map_err(|_| "凭证密钥状态异常。")?;
        if key.is_none() {
            let loaded = if let Some(value) = &self.environment_key {
                let bytes = Zeroizing::new(
                    BASE64_STANDARD
                        .decode(value.as_bytes())
                        .map_err(|_| "部署主密钥必须是 32 字节的 Base64 编码。")?,
                );
                Zeroizing::new(
                    <[u8; 32]>::try_from(bytes.as_slice())
                        .map_err(|_| "部署主密钥必须是 32 字节的 Base64 编码。")?,
                )
            } else {
                self.load_key(create)?
            };
            *key = Some(loaded);
        }
        use_key(key.as_ref().expect("loaded key"))
    }
    fn load_key(&self, create: bool) -> Result<Zeroizing<[u8; 32]>, String> {
        crate::paths::ensure_safe_absolute(&self.root)?;
        let path = self.root.join("native-master-key.bin");
        crate::paths::ensure_safe_absolute(&path)?;
        let read = || -> Result<Zeroizing<[u8; 32]>, String> {
            let mut file = File::open(&path).map_err(|_| "无法读取本地主密钥，已停止解密凭证。")?;
            let mut bytes = Zeroizing::new(Vec::new());
            Read::by_ref(&mut file)
                .take(33)
                .read_to_end(&mut bytes)
                .map_err(|_| "本地主密钥读取失败。")?;
            Ok(Zeroizing::new(
                <[u8; 32]>::try_from(bytes.as_slice())
                    .map_err(|_| "本地主密钥损坏，不能重新生成覆盖。")?,
            ))
        };
        match path.try_exists() {
            Ok(true) => return read(),
            Ok(false) if !create => return Err("本地主密钥缺失，请恢复原密钥后再使用凭证。".into()),
            Err(_) => return Err("无法确认本地主密钥状态。".into()),
            _ => {}
        }
        private_files::directory(&self.root)?;
        let mut key = Zeroizing::new([0_u8; 32]);
        getrandom::fill(&mut *key).map_err(|_| "无法生成本地主密钥。")?;
        let mut options = OpenOptions::new();
        options.write(true).create_new(true);
        #[cfg(unix)]
        {
            use std::os::unix::fs::OpenOptionsExt;
            options.mode(0o600);
        }
        let mut file = match options.open(&path) {
            Ok(file) => file,
            Err(cause) if cause.kind() == std::io::ErrorKind::AlreadyExists => return read(),
            Err(_) => return Err("无法创建受保护的本地主密钥。".into()),
        };
        file.write_all(&*key)
            .and_then(|_| file.sync_all())
            .map_err(|_| "本地主密钥写入失败，凭证未保存。")?;
        crate::paths::ensure_safe_absolute(&path)?;
        Ok(key)
    }
    pub fn protect(&self, name: &str, plain: &str) -> Result<String, String> {
        if plain.is_empty() {
            return Ok(String::new());
        }
        if plain.len() > 16 * 1024 || plain.starts_with(PREFIX) {
            return Err("凭证内容无效或超过 16 KiB。".into());
        }
        self.with_key(true, |key| {
            let cipher = Aes256Gcm::new_from_slice(key).map_err(|_| "本地主密钥长度无效。")?;
            let mut nonce = [0_u8; 12];
            getrandom::fill(&mut nonce).map_err(|_| "无法生成凭证随机数。")?;
            let encrypted = cipher
                .encrypt(
                    &Nonce::from(nonce),
                    Payload {
                        msg: plain.as_bytes(),
                        aad: name.as_bytes(),
                    },
                )
                .map_err(|_| "凭证加密失败。")?;
            let mut bytes = nonce.to_vec();
            bytes.extend(encrypted);
            Ok(format!("{PREFIX}{}", BASE64_STANDARD.encode(bytes)))
        })
    }
    pub fn unprotect(&self, name: &str, value: &str) -> Result<Zeroizing<String>, String> {
        if value.is_empty() {
            return Ok(Zeroizing::new(String::new()));
        }
        let encoded = value
            .strip_prefix(PREFIX)
            .filter(|v| v.len() < 24 * 1024)
            .ok_or("凭证格式无效，拒绝读取明文或未知格式。")?;
        let bytes = BASE64_STANDARD
            .decode(encoded)
            .map_err(|_| "加密凭证损坏。")?;
        if bytes.len() < 28 {
            return Err("加密凭证长度无效。".into());
        }
        self.with_key(false, |key| {
            let cipher = Aes256Gcm::new_from_slice(key).map_err(|_| "本地主密钥长度无效。")?;
            let nonce: [u8; 12] = bytes[..12].try_into().expect("validated nonce");
            let plaintext = cipher
                .decrypt(
                    &Nonce::from(nonce),
                    Payload {
                        msg: &bytes[12..],
                        aad: name.as_bytes(),
                    },
                )
                .map_err(|_| "凭证认证失败，请核对原始主密钥与备份。")?;
            String::from_utf8(plaintext)
                .map(Zeroizing::new)
                .map_err(|_| "凭证内容损坏。".into())
        })
    }
}
