use super::*;
#[path = "remote_index.rs"]
mod index;
pub(in crate::engine::team_backup) enum Target {
    WebDav(super::super::cloud::RecoveryTarget),
    #[cfg(feature = "s3-backup")]
    S3(super::super::s3::Target),
}
impl Target {
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        match self {
            Self::WebDav(t) => t.read_small(name, limit),
            #[cfg(feature = "s3-backup")]
            Self::S3(t) => t.read_small(name, limit),
        }
    }
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        match self {
            Self::WebDav(t) => t.create_small(name, bytes),
            #[cfg(feature = "s3-backup")]
            Self::S3(t) => t.create_small(name, bytes),
        }
    }
    fn delete(&self, name: &str) -> Result<()> {
        match self {
            Self::WebDav(t) => t.delete(name),
            #[cfg(feature = "s3-backup")]
            Self::S3(t) => t.delete(name),
        }
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        match self {
            Self::WebDav(t) => t.list_names(prefix, after, limit),
            #[cfg(feature = "s3-backup")]
            Self::S3(t) => t.list_names(prefix, after, limit),
        }
    }
}
/// A durable single-writer ownership record. It never expires automatically:
/// recovery on another A requires explicit fencing/release of the old writer.
/// Process concurrency on A is additionally guarded by the existing package lock.
pub(in crate::engine::team_backup) struct Remote {
    target: Target,
    owner: Vec<u8>,
    session: Vec<u8>,
    // WebDAV has no cursor listing. Snapshot immutable object names only while
    // this handle owns writer-session; live reader markers are never cached.
    names: index::RemoteNames,
}
impl Remote {
    pub fn open(target: Target, owner: &str, fenced: bool) -> Result<Self> {
        valid_id(owner)?;
        if let Some(existing) = target.read("writer-lock", 1024)? {
            if existing != owner.as_bytes() && fenced {
                target.delete("writer-session")?;
                target.delete("writer-lock")?;
                if !target.create("writer-lock", owner.as_bytes())? {
                    return Err(super::super::error(
                        429,
                        "仓库写入权正在被其它实例接管，请重新检查。",
                    ));
                }
            } else if existing != owner.as_bytes() {
                return Err(invalid(
                    "仓库属于另一部署实例。请先停止并隔离旧 A，由维护流程核验后移交仓库写入权。",
                ));
            }
        } else {
            let probe = format!("probe-{}", paths::nonce().map_err(unavailable)?);
            let result = (|| {
                if !target.create(&probe, b"first")?
                    || target.create(&probe, b"second")?
                    || target.read(&probe, 64)?.as_deref() != Some(b"first")
                {
                    return Err(invalid("目标不支持可靠的条件创建，不能作为恢复仓库。"));
                }
                Ok(())
            })();
            let cleanup = target.delete(&probe);
            result?;
            cleanup?;
            if !target.create("writer-lock", owner.as_bytes())? {
                return Err(invalid("仓库写入权已被另一实例取得，请重新检查。"));
            }
        }
        if fenced {
            target.delete("writer-session")?;
        }
        let session = paths::nonce().map_err(unavailable)?.into_bytes();
        if !target.create("writer-session", &session)? {
            return Err(super::super::error(
                429,
                "恢复仓库存在运行中或中断的写入会话。请确认所有旧写者已停止并隔离后，使用解除中断保护。",
            ));
        }
        let remote = Self {
            target,
            owner: owner.as_bytes().to_vec(),
            session,
            names: Default::default(),
        };
        remote.check()?;
        Ok(remote)
    }
}
impl Drop for Remote {
    fn drop(&mut self) {
        let _ = crate::operation::OperationScope::detached(std::time::Duration::from_secs(15)).run(
            || {
                if self.target.read("writer-session", 128)?.as_deref() == Some(&self.session) {
                    self.target.delete("writer-session")?;
                }
                Ok::<_, crate::api::ApiError>(())
            },
        );
    }
}
impl ObjectStore for Remote {
    fn check(&self) -> Result<()> {
        if self.target.read("writer-lock", 1024)?.as_deref() != Some(&self.owner)
            || self.target.read("writer-session", 128)?.as_deref() != Some(&self.session)
        {
            return Err(unavailable(
                "仓库写入权丢失，已停止传输和清理；请联系部署管理员核对所有权。",
            ));
        }
        Ok(())
    }
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        self.check()?;
        io::valid_name(name)?;
        self.target.read(name, limit)
    }
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        self.check()?;
        io::valid_name(name)?;
        let result = self.target.create(name, bytes)?;
        self.check()?;
        if result {
            self.names.changed(name, true)?;
        }
        Ok(result)
    }
    fn delete(&self, name: &str) -> Result<()> {
        self.check()?;
        io::valid_name(name)?;
        self.target.delete(name)?;
        self.names.changed(name, false)?;
        self.check()
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.check()?;
        let names = self.names.list(&self.target, prefix, after, limit)?;
        self.check()?;
        Ok(names)
    }
}
pub(in crate::engine::team_backup) struct Reader {
    target: Target,
    name: String,
    value: Vec<u8>,
    names: index::RemoteNames,
}
impl Reader {
    pub fn open(target: Target) -> Result<Self> {
        let value = paths::nonce().map_err(unavailable)?.into_bytes();
        let name = format!("reader-{}", paths::nonce().map_err(unavailable)?);
        if !target.create(&name, &value)? {
            return Err(invalid("无法取得恢复仓库读取保护。"));
        }
        let reader = Self {
            target,
            name,
            value,
            names: Default::default(),
        };
        reader.check()?;
        Ok(reader)
    }
}
impl ObjectStore for Reader {
    fn check(&self) -> Result<()> {
        if self.target.read("gc-lock", 128)?.is_some() {
            return Err(super::super::error(
                429,
                "恢复仓库正在回收，请稍后重新解锁。",
            ));
        }
        if self.target.read(&self.name, 1024)?.as_deref() != Some(self.value.as_slice()) {
            return Err(unavailable(
                "恢复仓库读取保护丢失，请停止清理任务后重新核验。",
            ));
        }
        Ok(())
    }
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        self.check()?;
        io::valid_name(name)?;
        self.target.read(name, limit)
    }
    fn create(&self, _: &str, _: &[u8]) -> Result<bool> {
        Err(invalid("维护读取会话不能向源仓库写入恢复点。"))
    }
    fn delete(&self, _: &str) -> Result<()> {
        Err(invalid("维护读取会话不能删除源仓库对象。"))
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.check()?;
        let names = self.names.list(&self.target, prefix, after, limit)?;
        self.check()?;
        Ok(names)
    }
}
impl Drop for Reader {
    fn drop(&mut self) {
        let _ = crate::operation::OperationScope::detached(std::time::Duration::from_secs(15))
            .run(|| self.target.delete(&self.name));
    }
}
