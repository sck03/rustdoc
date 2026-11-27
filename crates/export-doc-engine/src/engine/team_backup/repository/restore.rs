//! Stream verified objects directly into an explicit new file-library target, or
//! verify the original library without rewriting it. Activation uses the existing
//! database/filesystem recovery journal and exclusive maintenance boundary.
use super::super::package;
use super::*;
use crate::engine::file_store::{Backend, Configuration, FileStore};
use std::{
    fs,
    io::{BufRead, BufReader, Write},
};
const MANIFEST: &str = "recovery-stage.jsonl";
#[derive(Serialize, Deserialize)]
struct Entry {
    role: String,
    name: String,
    size: u64,
    digest: String,
}

pub(in crate::engine::team_backup) fn prepare(
    paths: &RuntimePaths,
    repo: &Repository,
    id: &str,
    marker: &Path,
    options: &serde_json::Value,
) -> Result<package::PreparedRestore> {
    let mode = options["fileMode"].as_str().unwrap_or("Local");
    let location = options["targetLocation"].as_str().unwrap_or("");
    let commit = repo.commit(id)?;
    package::prepare_snapshot(marker, |staging| {
        let mut config: Configuration = serde_json::from_value(commit.source_storage.clone())
            .map_err(|_| invalid("恢复点缺少有效生产文件库配置。"))?;
        let root = if mode == "Reconnect" {
            if matches!(&config.backend,Backend::Local{directory} if !directory.starts_with('@')) {
                return Err(invalid(
                    "原文件库位于旧 A 本机，不能在新 A 直接重接。请选择本机、登记位置或 S3 重建。",
                ));
            }
            if !location.is_empty() {
                return Err(invalid(
                    "重接原文件库使用原位置编号，请先在部署中登记对应位置。",
                ));
            }
            None
        } else if mode == "Local" || mode == "Directory" {
            config.identity = paths::nonce().map_err(unavailable)?;
            config.revision = 1;
            config.credential_revision = 1;
            config.version = 2;
            let root = if mode == "Directory" {
                let base = paths
                    .file_storage_roots
                    .get(location)
                    .ok_or_else(|| invalid("恢复目标位置尚未登记。"))?;
                paths::ensure_safe_absolute(base).map_err(invalid)?;
                if !base.is_dir() {
                    return Err(unavailable("恢复目标未挂载，不能写入替代目录。"));
                }
                let child = format!("recovered-{}", config.identity);
                config.backend = Backend::Local {
                    directory: format!("@{location}/{child}"),
                };
                base.join(child)
            } else {
                config.backend = Backend::Local {
                    directory: "Files".into(),
                };
                staging.join("Files")
            };
            config.medium_identity =
                paths::volume_identity(root.parent().unwrap()).map_err(unavailable)?;
            if root.try_exists()? {
                return Err(invalid("新恢复文件库目录已存在，禁止覆盖。"));
            }
            package::private_directory(&root)?;
            paths::atomic_write(&root.join(".store-id"), config.identity.as_bytes())
                .map_err(unavailable)?;
            Some(root)
        } else if mode == "S3" {
            #[cfg(not(feature = "s3-storage"))]
            return Err(super::super::unsupported("此构建不含 S3 文件库恢复能力。"));
            #[cfg(feature = "s3-storage")]
            {
                let settings = serde_json::from_value(options["s3"].clone())
                    .map_err(|_| invalid("请填写新的 S3 文件库连接配置。"))?;
                crate::engine::s3_transport::validate(&settings)?;
                config.identity = paths::nonce().map_err(unavailable)?;
                config.revision = 1;
                config.credential_revision = 1;
                config.version = 2;
                config.medium_identity = String::new();
                config.backend = Backend::S3 {
                    settings,
                    secret: String::new(),
                    token: String::new(),
                };
                None
            }
        } else {
            return Err(invalid("请选择接回原库、本机、登记位置或 S3 重建。"));
        };
        if mode != "Reconnect" {
            for value in config.archive_locations.values_mut() {
                *value = None;
            }
        }
        package::private_directory(&staging.join("Configuration"))?;
        paths::atomic_write(
            &staging.join("Configuration/file-storage.json"),
            &serde_json::to_vec(&config)?,
        )
        .map_err(unavailable)?;
        let mut staged_paths = paths.clone();
        staged_paths.data_root = staging.into();
        let manifest = fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(staging.join(MANIFEST))?;
        #[cfg(feature = "s3-storage")]
        let upload_journal = paths
            .data_root
            .join("Recovery/Uploads")
            .join(&config.identity);
        let mut visitor = Apply {
            #[cfg(feature = "s3-storage")]
            upload_journal,
            paths: staged_paths,
            root,
            config,
            reconnect: mode == "Reconnect",
            files: None,
            current: None,
            current_root: staging.into(),
            manifest,
            master: false,
            database: false,
            last_attachment: String::new(),
            names: std::collections::BTreeSet::new(),
            #[cfg(feature = "s3-storage")]
            options: options.clone(),
            #[cfg(feature = "s3-storage")]
            remote: None,
            #[cfg(feature = "s3-storage")]
            upload: None,
        };
        // A persistent intention remains for an interrupted external-library copy.
        let recovery = paths.data_root.join("Recovery");
        package::private_directory(&recovery)?;
        let intent = recovery.join(format!("{}.json", visitor.config.identity));
        paths::atomic_write(&intent,&serde_json::to_vec(&serde_json::json!({"state":"ApplyingFiles","reconnect":mode=="Reconnect","pointId":id,"storage":visitor.config,"stagingDirectory":staging.file_name().unwrap().to_string_lossy()}))?).map_err(unavailable)?;
        repo.verify(id, &mut visitor)?;
        if !visitor.master || !visitor.database {
            return Err(invalid("恢复点缺少数据库或应用主密钥。"));
        }
        visitor.prepare_remote()?;
        let configuration_path = staging.join("Configuration/file-storage.json");
        let (size, digest) = crate::engine::backup_io::digest_file(&configuration_path)?;
        serde_json::to_writer(
            &mut visitor.manifest,
            &Entry {
                role: "Configuration".into(),
                name: "Configuration/file-storage.json".into(),
                size,
                digest,
            },
        )?;
        visitor.manifest.write_all(b"\n")?;
        visitor.manifest.sync_all()?;
        let digest = crate::engine::backup_io::digest_file(&staging.join(MANIFEST))?.1;
        paths::atomic_write(&intent,&serde_json::to_vec(&serde_json::json!({"state":"Ready","reconnect":mode=="Reconnect","pointId":id,"storage":visitor.config,"manifestSha256":digest,"stagingDirectory":staging.file_name().unwrap().to_string_lossy()}))?).map_err(unavailable)?;
        if commit.provider == "SQLite" {
            export_doc_storage::verify_sqlite_backup(&staging.join(package::SQLITE))?;
        }
        Ok(package::SnapshotRestore::Repository {
            provider: commit.provider,
            sha256: digest,
            point_id: id.into(),
        })
    })
}
struct Apply {
    #[cfg(feature = "s3-storage")]
    upload_journal: PathBuf,
    paths: RuntimePaths,
    root: Option<PathBuf>,
    config: Configuration,
    reconnect: bool,
    files: Option<FileStore>,
    current: Option<fs::File>,
    current_root: PathBuf,
    manifest: fs::File,
    master: bool,
    database: bool,
    last_attachment: String,
    names: std::collections::BTreeSet<String>,
    #[cfg(feature = "s3-storage")]
    options: serde_json::Value,
    #[cfg(feature = "s3-storage")]
    remote: Option<crate::engine::s3_transport::Target>,
    #[cfg(feature = "s3-storage")]
    upload: Option<crate::engine::s3_transport::StreamUpload>,
}
impl Visitor for Apply {
    fn begin(&mut self, role: &str, name: &str, size: u64, digest: &str) -> Result<()> {
        if role == "Attachment" {
            let archive_identity = attachment_identity(name, digest)?;
            if digest <= self.last_attachment.as_str() {
                return Err(invalid("恢复点附件清单重复或顺序无效。"));
            }
            self.last_attachment = digest.into();
            if !self.master || !self.database {
                return Err(invalid("附件前缺少数据库或主密钥。"));
            }
            self.prepare_remote()?;
            if self.reconnect {
                if self.files.is_none() {
                    self.files = Some(FileStore::open(&self.paths, true)?);
                }
                self.files.as_ref().unwrap().stream_object(
                    &export_doc_storage::FileObject {
                        digest: digest.into(),
                        archive_identity: archive_identity.into(),
                        size,
                    },
                    &mut std::io::sink(),
                )?;
                self.current = None;
            } else if self.root.is_none() {
                #[cfg(feature = "s3-storage")]
                {
                    let remote = self
                        .remote
                        .as_ref()
                        .ok_or_else(|| invalid("S3 文件库尚未准备。"))?;
                    if size == 0 {
                        if !remote.create_small(digest, b"")? {
                            return Err(invalid("新 S3 文件库出现已有对象，已停止恢复。"));
                        }
                    } else {
                        self.upload = Some(crate::engine::s3_transport::StreamUpload::new(
                            remote.clone(),
                            digest,
                            size,
                            digest,
                        )?);
                    }
                    self.current = None;
                }
                #[cfg(not(feature = "s3-storage"))]
                return Err(super::super::unsupported("此构建不含 S3 文件库恢复能力。"));
            } else {
                let root = self.root.as_ref().unwrap();
                if paths::volume_identity(root).map_err(unavailable)? != self.config.medium_identity
                {
                    return Err(unavailable("恢复目标介质改变，已停止写入。"));
                }
                let path = root.join("objects").join(&digest[..2]).join(digest);
                paths::ensure_safe_absolute(&path).map_err(invalid)?;
                package::private_directory(path.parent().unwrap())?;
                self.current_root = path.parent().unwrap().into();
                self.current = Some(
                    fs::OpenOptions::new()
                        .write(true)
                        .create_new(true)
                        .open(path)?,
                );
            }
        } else {
            if !self.names.insert(name.to_owned()) || self.names.len() > sealed::MAX_FILES {
                return Err(invalid("恢复点受管文件重复或数量过多。"));
            }
            match role {
                "Database"
                    if !self.database && (name == package::SQLITE || name == package::POSTGRES) =>
                {
                    self.database = true
                }
                "MasterKey" if !self.master && name == package::KEY && size == 32 => {
                    self.master = true
                }
                "Template" if name.starts_with("Templates/") => {}
                _ => return Err(invalid("恢复点包含不允许安装的系统文件。")),
            }
            let path = self.paths.data_root.join(name);
            paths::ensure_safe_absolute(&path).map_err(invalid)?;
            package::private_directory(path.parent().unwrap())?;
            self.current_root = path.parent().unwrap().into();
            self.current = Some(
                fs::OpenOptions::new()
                    .write(true)
                    .create_new(true)
                    .open(path)?,
            );
        }
        let row = Entry {
            role: role.into(),
            name: name.into(),
            size,
            digest: digest.into(),
        };
        serde_json::to_writer(&mut self.manifest, &row)?;
        self.manifest.write_all(b"\n")?;
        Ok(())
    }
    fn data(&mut self, bytes: &[u8]) -> Result<()> {
        if let Some(root) = &self.root {
            if paths::volume_identity(root).map_err(unavailable)? != self.config.medium_identity {
                return Err(unavailable("恢复过程中目标介质掉线或已更换。"));
            }
        }
        if let Some(file) = self.current.as_mut() {
            let budget = self.paths.disk_space.at(self.current_root.clone());
            budget.writer(file)?.write_all(bytes)?;
        }
        #[cfg(feature = "s3-storage")]
        if let Some(upload) = self.upload.as_mut() {
            upload.write_all(bytes)?;
        }
        Ok(())
    }
    fn end(&mut self) -> Result<()> {
        if let Some(file) = self.current.take() {
            file.sync_all()?;
        }
        #[cfg(feature = "s3-storage")]
        if let Some(upload) = self.upload.take() {
            upload.finish()?;
        }
        Ok(())
    }
}
impl Apply {
    fn prepare_remote(&mut self) -> Result<()> {
        #[cfg(feature = "s3-storage")]
        if self.remote.is_none() {
            if let Backend::S3 {
                settings,
                secret,
                token,
            } = &mut self.config.backend
            {
                let protector = crate::secrets::Protector::new(&self.paths.data_root);
                let password = self.options["secretAccessKey"]
                    .take()
                    .as_str()
                    .unwrap_or("")
                    .to_owned();
                let password = zeroize::Zeroizing::new(password);
                let session = zeroize::Zeroizing::new(
                    self.options["sessionToken"]
                        .take()
                        .as_str()
                        .unwrap_or("")
                        .to_owned(),
                );
                if !password.is_empty() {
                    if self.reconnect && self.options["s3"].is_object() {
                        *settings = serde_json::from_value(self.options["s3"].clone())
                            .map_err(|_| invalid("重接 S3 的连接配置无效。"))?;
                    }
                    *secret = protector
                        .protect("file-storage-secret", &password)
                        .map_err(unavailable)?;
                    *token = if session.is_empty() {
                        String::new()
                    } else {
                        protector
                            .protect("file-storage-token", &session)
                            .map_err(unavailable)?
                    };
                }
                let password = protector
                    .unprotect("file-storage-secret", secret)
                    .map_err(unavailable)?;
                let session = if token.is_empty() {
                    zeroize::Zeroizing::new(String::new())
                } else {
                    protector
                        .unprotect("file-storage-token", token)
                        .map_err(unavailable)?
                };
                let target = crate::engine::s3_transport::Target::new(
                    settings.clone(),
                    &self.config.identity,
                    &password,
                    &session,
                )?
                .with_journal(self.upload_journal.clone());
                if !self.reconnect
                    && (!target.list_names("", "", 1)?.is_empty()
                        || !target.create_small("store-id", self.config.identity.as_bytes())?)
                {
                    return Err(invalid("新的 S3 文件库前缀不为空，禁止覆盖。"));
                }
                paths::atomic_write(
                    &self.paths.data_root.join("Configuration/file-storage.json"),
                    &serde_json::to_vec(&self.config)?,
                )
                .map_err(unavailable)?;
                self.remote = Some(target);
            }
        }
        Ok(())
    }
}
pub(in crate::engine::team_backup) fn validate(
    paths: &RuntimePaths,
    staging: &Path,
    digest: &str,
    provider: &str,
) -> Result<()> {
    if crate::engine::backup_io::digest_file(&staging.join(MANIFEST))?.1 != digest {
        return Err(invalid("恢复暂存清单已改变，禁止激活。"));
    }
    let mut staged_paths = paths.clone();
    staged_paths.data_root = staging.into();
    let files = FileStore::open(&staged_paths, true)?;
    let mut input = BufReader::new(fs::File::open(staging.join(MANIFEST))?);
    let mut line = String::new();
    let mut database = false;
    let mut master = false;
    loop {
        line.clear();
        use std::io::Read;
        let count = input.by_ref().take(8193).read_line(&mut line)?;
        if count == 0 {
            break;
        }
        if count > 8192 || !line.ends_with('\n') {
            return Err(invalid("恢复暂存清单行无效。"));
        }
        let row: Entry = serde_json::from_str(&line)?;
        if row.role == "Attachment" {
            files.stream_object(
                &export_doc_storage::FileObject {
                    archive_identity: attachment_identity(&row.name, &row.digest)?.into(),
                    digest: row.digest,
                    size: row.size,
                },
                &mut std::io::sink(),
            )?;
        } else {
            let path = staging.join(&row.name);
            paths::ensure_safe_absolute(&path).map_err(invalid)?;
            if crate::engine::backup_io::digest_file(&path)? != (row.size, row.digest) {
                return Err(invalid("恢复暂存文件已改变。"));
            }
            if row.role == "Database" {
                database = row.name
                    == if provider == "SQLite" {
                        package::SQLITE
                    } else {
                        package::POSTGRES
                    };
            }
            if row.role == "MasterKey" {
                master = row.name == package::KEY && row.size == 32;
            }
        }
    }
    if !database || !master {
        return Err(invalid("恢复暂存缺少匹配的数据库或主密钥。"));
    }
    Ok(())
}

fn attachment_identity<'a>(name: &'a str, digest: &str) -> Result<&'a str> {
    if name == digest {
        return Ok("");
    }
    let (identity, tail) = name
        .split_once('/')
        .ok_or_else(|| invalid("恢复点附件路径无效。"))?;
    super::valid_id(identity)?;
    if tail != digest {
        return Err(invalid("恢复点附件路径与摘要不符。"));
    }
    Ok(identity)
}
