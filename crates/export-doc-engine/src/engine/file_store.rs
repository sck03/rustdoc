//! Immutable business objects. Only metadata participates in database transactions.
//! A prepared object is bound to its storage generation, so a concurrent migration
//! cannot publish a reference to an object left behind at the old location.
use super::error::{Result, conflict, invalid, unavailable};
use crate::paths::{self, RuntimePaths};
use export_doc_storage::{FileInfo, FileObject};
use serde::{Deserialize, Serialize};
pub(super) mod admin;
mod archive;
mod cache;
mod credentials;
pub(super) mod incremental;
mod journal;
mod migration;
pub(super) mod policy;
mod remote;
#[cfg(test)]
mod tests;
pub(super) use archive::verify_local_inventory;
pub(super) use archive::{export as export_archive, restore_archive};
use std::{
    fs,
    io::{Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    sync::RwLock,
};

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Policy {
    pub file_bytes: u64,
    pub request_bytes: u64,
    pub request_files: u32,
    pub quota_bytes: u64,
}
impl Default for Policy {
    fn default() -> Self {
        Self {
            file_bytes: 100 * 1024 * 1024,
            request_bytes: 500 * 1024 * 1024,
            request_files: 100,
            quota_bytes: 0,
        }
    }
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum Backend {
    Local {
        directory: String,
    },
    S3 {
        settings: export_doc_domain::backup_plan::S3Settings,
        secret: String,
        token: String,
    },
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Configuration {
    pub version: u32,
    pub revision: u64,
    pub credential_revision: u64,
    pub identity: String,
    pub backend: Backend,
    pub policy: Policy,
}
pub struct PreparedObject {
    pub object: FileObject,
    pub(super) identity: String,
    _lease: journal::UploadLease,
}
impl PreparedObject {
    pub(super) fn intent_path(&self) -> PathBuf {
        self._lease.path.clone()
    }
}
pub struct FileStore {
    root: PathBuf,
    roots: std::collections::BTreeMap<String, PathBuf>,
    cache: PathBuf,
    configuration_path: PathBuf,
    protector: crate::secrets::Protector,
    state: RwLock<Configuration>,
    uploads: journal::Uploads,
    space: std::sync::Arc<crate::temporary_space::Budget>,
    downloads: cache::Cache,
    #[cfg(feature = "s3-storage")]
    agent: ureq::Agent,
}
impl FileStore {
    pub fn open(paths: &RuntimePaths, registered: bool) -> Result<Self> {
        let path = paths
            .data_root
            .join("Configuration")
            .join("file-storage.json");
        paths::ensure_safe_absolute(&path).map_err(unavailable)?;
        let config: Configuration = if path.try_exists()? {
            let file = fs::File::open(&path)?;
            if file.metadata()?.len() > 64 * 1024 {
                return Err(unavailable("文件存储配置过大。"));
            }
            serde_json::from_reader(file).map_err(|_| unavailable("文件存储配置损坏。"))?
        } else {
            if registered {
                return Err(unavailable(
                    "数据库已有文件记录但存储配置缺失，已停止启动。请恢复完整备份。",
                ));
            }
            let config = Configuration {
                version: 1,
                revision: 1,
                credential_revision: 1,
                identity: paths::nonce().map_err(unavailable)?,
                backend: Backend::Local {
                    directory: "Files".into(),
                },
                policy: Policy::default(),
            };
            crate::secrets::private_directory(path.parent().unwrap()).map_err(unavailable)?;
            let root = paths.data_root.join("Files");
            paths::ensure_safe_absolute(&root).map_err(unavailable)?;
            if root.try_exists()? && fs::read_dir(&root)?.next().is_some() {
                return Err(unavailable("文件存储缺少配置，不能接管现有文件目录。"));
            }
            crate::secrets::private_directory(&root).map_err(unavailable)?;
            paths::atomic_write(&root.join(".store-id"), config.identity.as_bytes())
                .map_err(unavailable)?;
            paths::atomic_write(&path, &serde_json::to_vec(&config)?).map_err(unavailable)?;
            config
        };
        if config.version != 1
            || config.identity.len() != 32
            || !config.identity.bytes().all(|c| c.is_ascii_hexdigit())
        {
            return Err(unavailable("文件存储身份无效。"));
        }
        config.policy.validate()?;
        let store = Self {
            root: paths.data_root.clone(),
            roots: paths.file_storage_roots.clone(),
            cache: paths.cache_root.clone(),
            configuration_path: path,
            protector: crate::secrets::Protector::new(&paths.data_root),
            state: RwLock::new(config),
            uploads: Default::default(),
            space: paths.disk_space.clone(),
            downloads: cache::Cache::open(&paths.cache_root.join("FileDownloads"))?,
            #[cfg(feature = "s3-storage")]
            agent: export_doc_network::storage_pooled_agent(
                std::time::Duration::from_secs(super::backup_io::TIMEOUT_SECONDS),
                || crate::operation::check().is_ok(),
            ),
        };
        // Keep the authenticated administration surface available when remote
        // credentials expire. Every remote file operation still fails closed.
        let config = store.configuration()?;
        match &config.backend {
            Backend::Local { .. } => store.local_identity(&config)?,
            Backend::S3 { .. } => {
                store.target(&config)?;
            }
        }
        Ok(store)
    }
    pub fn configuration(&self) -> Result<Configuration> {
        self.state
            .read()
            .map(|s| s.clone())
            .map_err(|_| unavailable("文件存储状态异常。"))
    }
    fn local_identity(&self, config: &Configuration) -> Result<()> {
        let root = self.local_root(config)?;
        let marker = root.join(".store-id");
        paths::ensure_safe_absolute(&marker).map_err(unavailable)?;
        let mut bytes = Vec::new();
        fs::File::open(marker)
            .map_err(|_| unavailable("文件存储未挂载或身份标记缺失。"))?
            .take(65)
            .read_to_end(&mut bytes)?;
        if bytes != config.identity.as_bytes() {
            return Err(unavailable("文件存储身份不匹配。"));
        }
        Ok(())
    }
    fn target(&self, config: &Configuration) -> Result<remote::Target> {
        let Backend::S3 {
            settings,
            secret,
            token,
        } = &config.backend
        else {
            return Err(invalid("当前不是 S3 存储。"));
        };
        let secret = self
            .protector
            .unprotect("file-storage-secret", secret)
            .map_err(unavailable)?;
        let token = if token.is_empty() {
            zeroize::Zeroizing::new(String::new())
        } else {
            self.protector
                .unprotect("file-storage-token", token)
                .map_err(unavailable)?
        };
        let target = remote::Target::new(settings.clone(), &config.identity, &secret, &token)?;
        #[cfg(feature = "s3-storage")]
        let target = target
            .with_agent(self.agent.clone())
            .with_journal(self.configuration_path.parent().unwrap().join("S3Uploads"));
        Ok(target)
    }
    pub fn health(&self) -> Result<()> {
        let state = self.configuration()?;
        self.check_identity(&state)
    }
    fn check_identity(&self, state: &Configuration) -> Result<()> {
        match &state.backend {
            Backend::Local { .. } => self.local_identity(&state),
            Backend::S3 { .. } => {
                let mut marker = Vec::new();
                self.target(state)?.download("store-id", &mut marker, 64)?;
                if marker != state.identity.as_bytes() {
                    return Err(unavailable("S3 文件存储身份不匹配。"));
                }
                Ok(())
            }
        }
    }
    pub(super) fn verify_inventory(
        &self,
        mut page: impl FnMut(&str) -> Result<Vec<FileObject>>,
    ) -> Result<()> {
        let mut cursor = String::new();
        loop {
            let objects = page(&cursor)?;
            if objects.is_empty() {
                return Ok(());
            }
            for object in objects {
                self.open_object(&object)?;
                cursor = object.digest;
            }
        }
    }
    fn local_root(&self, config: &Configuration) -> Result<PathBuf> {
        let Backend::Local { directory } = &config.backend else {
            return Err(invalid("当前不是本地存储。"));
        };
        let (base, directory) = if let Some(alias) = directory.strip_prefix('@') {
            let (name, relative) = alias.split_once('/').unwrap_or((alias, ""));
            let base = self
                .roots
                .get(name)
                .ok_or_else(|| unavailable("独立文件存储位置未在启动配置中登记。"))?;
            if !base.is_dir() {
                return Err(unavailable("独立文件存储未挂载。"));
            }
            (base, relative)
        } else {
            let first = directory.split('/').next().unwrap_or("");
            if [
                "Database",
                "Security",
                "Configuration",
                "Backups",
                "Cache",
                "Logs",
                "Templates",
                "Locks",
            ]
            .iter()
            .any(|name| name.eq_ignore_ascii_case(first))
            {
                return Err(invalid("不能将系统受管目录用作文件存储。"));
            }
            (&self.root, directory.as_str())
        };
        if (directory.is_empty() && base == &self.root)
            || directory
                .split('/')
                .any(|part| !directory.is_empty() && !paths::valid_file_name(part))
        {
            return Err(invalid("本地存储须使用受管相对目录。"));
        }
        let root = base.join(directory);
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        Ok(root)
    }
    fn object_path(&self, config: &Configuration, object: &FileObject) -> Result<PathBuf> {
        validate(object)?;
        let path = self
            .local_root(config)?
            .join("objects")
            .join(&object.digest[..2])
            .join(&object.digest);
        paths::ensure_safe_absolute(&path).map_err(unavailable)?;
        Ok(path)
    }
    pub fn prepare(&self, bytes: &[u8]) -> Result<PreparedObject> {
        self.prepare_reader(&mut std::io::Cursor::new(bytes), bytes.len() as u64)
    }
    pub fn prepare_reader(
        &self,
        input: &mut (impl Read + Seek),
        limit: u64,
    ) -> Result<PreparedObject> {
        let config = self.configuration()?;
        input.seek(SeekFrom::Start(0))?;
        let (size, digest) = super::backup_io::copy_and_hash(input, &mut std::io::sink(), limit)?;
        let object = FileObject { size, digest };
        validate(&object)?;
        let lease = self.begin_upload(&config, &object)?;
        input.seek(SeekFrom::Start(0))?;
        match &config.backend {
            Backend::Local { .. } => {
                self.local_identity(&config)?;
                let path = self.object_path(&config, &object)?;
                if path.try_exists()? {
                    verify(&mut fs::File::open(&path)?, &object)?;
                } else {
                    crate::secrets::private_directory(path.parent().unwrap())
                        .map_err(unavailable)?;
                    paths::atomic_write_with(&path, |output| {
                        let mut output = self
                            .space
                            .at(path.parent().unwrap().to_path_buf())
                            .writer(output)
                            .map_err(|e| e.to_string())?;
                        let actual =
                            super::backup_io::copy_and_hash(input, &mut output, object.size)
                                .map_err(|e| e.to_string())?;
                        if actual != (object.size, object.digest.clone()) {
                            return Err("源文件在写入时发生变化。".into());
                        }
                        Ok(())
                    })
                    .map_err(unavailable)?;
                }
            }
            Backend::S3 { .. } => {
                self.check_identity(&config)?;
                self.target(&config)?.upload_file(
                    input,
                    &object.digest,
                    object.size,
                    &object.digest,
                )?;
            }
        }
        Ok(PreparedObject {
            object,
            identity: config.identity,
            _lease: lease,
        })
    }
    pub fn check_prepared(&self, object: &PreparedObject) -> Result<()> {
        if self.configuration()?.identity != object.identity {
            return Err(conflict("文件存储已切换，请重新提交上传。"));
        }
        Ok(())
    }
    pub fn open_object(&self, object: &FileObject) -> Result<fs::File> {
        validate(object)?;
        let config = self.configuration()?;
        let mut file = match &config.backend {
            Backend::Local { .. } => {
                self.local_identity(&config)?;
                fs::File::open(self.object_path(&config, object)?)
                    .map_err(|_| unavailable("已登记文件对象缺失或不可读。"))?
            }
            Backend::S3 { .. } => {
                return self.downloads.download(self, &config, object);
            }
        };
        verify(&mut file, object)?;
        file.seek(SeekFrom::Start(0))?;
        Ok(file)
    }
    pub fn load(&self, info: &FileInfo, limit: usize) -> Result<super::tasks::FileOutput> {
        if info.object.size > limit as u64 {
            return Err(invalid("该文件须使用流式下载，不能完整载入内存。"));
        }
        let mut file = self.open_object(&info.object)?;
        let mut content = Vec::with_capacity(info.object.size as usize);
        file.read_to_end(&mut content)?;
        Ok(super::tasks::FileOutput {
            file_name: info.file_name.clone(),
            media_type: info.media_type.clone(),
            content,
        })
    }
    pub fn download(&self, info: &FileInfo) -> Result<super::backup_io::Download> {
        Ok(super::backup_io::Download::File {
            file_name: info.file_name.clone(),
            media_type: info.media_type.clone(),
            file: self.open_object(&info.object)?,
            size: info.object.size,
        })
    }
}
fn validate(object: &FileObject) -> Result<()> {
    if object.size == 0
        || object.digest.len() != 64
        || !object
            .digest
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(unavailable("文件对象长度或摘要无效。"));
    }
    Ok(())
}
fn verify(file: &mut fs::File, object: &FileObject) -> Result<()> {
    if file.metadata()?.len() != object.size
        || super::backup_io::copy_and_hash(file, &mut std::io::sink(), object.size)?
            != (object.size, object.digest.clone())
    {
        return Err(unavailable("文件对象完整性校验失败。"));
    }
    Ok(())
}
