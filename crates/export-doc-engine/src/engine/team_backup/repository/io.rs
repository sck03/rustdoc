//! Storage capabilities for the recovery repository. Object names are flat and
//! bounded so all adapters share exactly the same namespace and safety checks.
use super::names::{Names, Namespace};
use super::*;
use fs2::FileExt;
use std::fs;

pub(in crate::engine::team_backup) trait ObjectStore:
    Send + Sync
{
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>>;
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool>;
    fn delete(&self, name: &str) -> Result<()>;
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>>;
    fn check(&self) -> Result<()>;
}
pub(in crate::engine::team_backup) struct Directory {
    root: PathBuf,
    medium: String,
    _lock: fs::File,
    // Valid only while this handle owns the exclusive OS repository lock.
    names: Names,
    #[cfg(test)]
    scans: std::sync::atomic::AtomicUsize,
}
impl Directory {
    pub fn open(root: PathBuf, expected_medium: Option<&str>) -> Result<Self> {
        paths::ensure_safe_absolute(&root).map_err(invalid)?;
        if !root.is_dir() {
            return Err(unavailable(
                "备份位置未挂载或目录不存在，请连接登记的存储设备。",
            ));
        }
        let medium = paths::volume_identity(&root).map_err(unavailable)?;
        if expected_medium.is_some_and(|value| value != medium) {
            return Err(unavailable(
                "备份介质身份已改变，不能在替代挂载目录写入恢复点。",
            ));
        }
        let lock_path = root.join(".repository-lock");
        paths::ensure_safe_absolute(&lock_path).map_err(invalid)?;
        let lock = fs::OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(lock_path)?;
        lock.try_lock_exclusive().map_err(|_| {
            super::super::error(429, "备份仓库正在被备份、恢复或清理使用，请稍后操作。")
        })?;
        Ok(Self {
            root,
            medium,
            _lock: lock,
            names: Names::default(),
            #[cfg(test)]
            scans: std::sync::atomic::AtomicUsize::new(0),
        })
    }
    fn path(&self, name: &str) -> Result<PathBuf> {
        self.check()?;
        valid_name(name)?;
        let path = self.root.join(Namespace::of(name).directory()).join(name);
        paths::ensure_safe_absolute(&path).map_err(invalid)?;
        Ok(path)
    }
}
impl ObjectStore for Directory {
    fn check(&self) -> Result<()> {
        crate::operation::check()?;
        paths::ensure_safe_absolute(&self.root).map_err(invalid)?;
        if !self.root.is_dir()
            || paths::volume_identity(&self.root).map_err(unavailable)? != self.medium
        {
            return Err(unavailable(
                "备份介质掉线或身份改变，已停止操作；请接回原设备后重新核验。",
            ));
        }
        Ok(())
    }
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        use std::io::Read;
        let path = self.path(name)?;
        let file = match fs::File::open(&path) {
            Ok(file) => file,
            Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => return Ok(None),
            Err(cause) => return Err(cause.into()),
        };
        let mut bytes = Vec::new();
        file.take(limit as u64 + 1).read_to_end(&mut bytes)?;
        if bytes.len() > limit {
            return Err(invalid("恢复点对象长度超过格式上限。"));
        }
        self.check()?;
        Ok(Some(bytes))
    }
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        use std::io::Write;
        let path = self.path(name)?;
        if path.try_exists()? {
            return Ok(false);
        }
        if matches!(Namespace::of(name), Namespace::Objects) {
            fs::create_dir_all(path.parent().unwrap())?;
            paths::ensure_safe_absolute(path.parent().unwrap()).map_err(invalid)?;
        }
        let pending = self.path(&format!("pending-{}", paths::nonce().map_err(unavailable)?))?;
        let result: Result<bool> = (|| {
            let mut file = fs::OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&pending)?;
            file.write_all(bytes)?;
            file.sync_all()?;
            self.check()?;
            // All writers hold the same OS lock. Rename also works on removable
            // filesystems without hard-link support; no existing object is replaced.
            if path.try_exists()? {
                return Ok(false);
            }
            fs::rename(&pending, &path)?;
            self.names.changed(name, true)?;
            Ok(true)
        })();
        if pending.try_exists()? {
            fs::remove_file(&pending)?;
        }
        let created = result?;
        #[cfg(unix)]
        {
            fs::File::open(path.parent().unwrap())?.sync_all()?;
            fs::File::open(&self.root)?.sync_all()?;
        }
        self.check()?;
        Ok(created)
    }
    fn delete(&self, name: &str) -> Result<()> {
        let path = self.path(name)?;
        match fs::remove_file(&path) {
            Ok(()) => {}
            Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => {}
            Err(cause) => return Err(cause.into()),
        }
        self.names.changed(name, false)?;
        #[cfg(unix)]
        if path.parent().unwrap().try_exists()? {
            fs::File::open(path.parent().unwrap())?.sync_all()?;
        }
        self.check()
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.check()?;
        let names = self.names.list(prefix, after, limit, |space| {
            #[cfg(test)]
            self.scans
                .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
            let directory = self.root.join(space.directory());
            paths::ensure_safe_absolute(&directory).map_err(invalid)?;
            if !directory.try_exists()? {
                return Ok(Vec::new());
            }
            let mut names = Vec::new();
            for entry in fs::read_dir(&directory)? {
                crate::operation::check()?;
                let entry = entry?;
                let name = entry
                    .file_name()
                    .into_string()
                    .map_err(|_| invalid("仓库含无效文件名。"))?;
                paths::ensure_safe_absolute(&entry.path()).map_err(invalid)?;
                if matches!(space, Namespace::Metadata)
                    && name == "objects"
                    && entry.file_type()?.is_dir()
                {
                    continue;
                }
                valid_name(&name)?;
                if !entry.file_type()?.is_file() || Namespace::of(&name) as usize != space as usize
                {
                    return Err(invalid("仓库含布局不符的对象，请使用当前格式的新仓库。"));
                }
                names.push(name);
                if names.len() > 2_000_000 {
                    return Err(invalid("仓库目录索引超过两百万对象预算，请分库管理。"));
                }
            }
            Ok(names)
        })?;
        for name in &names {
            let path = self.path(name)?;
            if !fs::metadata(path)?.is_file() {
                return Err(invalid("仓库对象不是普通文件。"));
            }
        }
        self.check()?;
        Ok(names)
    }
}

#[cfg(test)]
#[path = "tests/io.rs"]
mod tests;

pub(super) fn valid_name(name: &str) -> Result<()> {
    if name.is_empty()
        || name.len() > 128
        || !name
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, b'-' | b'.'))
        || name == "."
        || name == ".."
    {
        return Err(invalid("恢复仓库对象名称无效。"));
    }
    Ok(())
}
