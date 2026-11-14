//! Process-local verified objects. Independent handles serve overlapping Range
//! requests; eviction unlinks only files created by this cache instance.
use super::*;
use std::{collections::BTreeMap, sync::Mutex, time::SystemTime};
const CAPACITY: u64 = 256 * 1024 * 1024;
struct Entry {
    path: PathBuf,
    size: u64,
    modified: SystemTime,
    used: u64,
}
impl Drop for Entry {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}
#[derive(Default)]
struct State {
    entries: BTreeMap<String, Entry>,
    used: u64,
    size: u64,
}
#[derive(Default)]
pub(super) struct Cache(Mutex<State>);
impl Cache {
    pub(super) fn open(root: &Path) -> Result<Self> {
        paths::ensure_safe_absolute(root).map_err(unavailable)?;
        if root.try_exists()? {
            for entry in fs::read_dir(root)? {
                let path = entry?.path();
                let stale = path
                    .file_name()
                    .and_then(|name| name.to_str())
                    .and_then(|name| name.strip_prefix("verified-"))
                    .is_some_and(|name| {
                        name.len() == 32 && name.bytes().all(|byte| byte.is_ascii_hexdigit())
                    });
                if stale {
                    paths::ensure_safe_absolute(&path).map_err(unavailable)?;
                    fs::remove_file(path)?;
                }
            }
        }
        Ok(Self::default())
    }
    pub(super) fn get(&self, object: &FileObject) -> Result<Option<fs::File>> {
        let mut state = self
            .0
            .lock()
            .map_err(|_| unavailable("文件缓存状态异常。"))?;
        state.used = state.used.wrapping_add(1);
        let used = state.used;
        let Some(entry) = state.entries.get_mut(&object.digest) else {
            return Ok(None);
        };
        paths::ensure_safe_absolute(&entry.path).map_err(unavailable)?;
        let file = fs::File::open(&entry.path)?;
        let metadata = file.metadata()?;
        if metadata.len() != object.size || metadata.modified()? != entry.modified {
            let old = state.entries.remove(&object.digest).unwrap();
            state.size -= old.size;
            return Ok(None);
        }
        entry.used = used;
        Ok(Some(file))
    }
    pub(super) fn download(
        &self,
        store: &FileStore,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<fs::File> {
        if let Some(file) = self.get(object)? {
            return Ok(file);
        }
        if object.size > CAPACITY {
            return store.download_uncached(config, object);
        }
        let root = store.cache.join("FileDownloads");
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        let path = root.join(format!("verified-{}", paths::nonce().map_err(unavailable)?));
        let mut file = fs::OpenOptions::new()
            .create_new(true)
            .read(true)
            .write(true)
            .open(&path)?;
        let mut entry = Entry {
            path,
            size: object.size,
            modified: SystemTime::UNIX_EPOCH,
            used: 0,
        };
        store.download_into(config, object, &mut file)?;
        entry.modified = file.metadata()?.modified()?;
        file.seek(SeekFrom::Start(0))?;
        let mut state = self
            .0
            .lock()
            .map_err(|_| unavailable("文件缓存状态异常。"))?;
        if let Some(old) = state.entries.remove(&object.digest) {
            state.size -= old.size;
        }
        while state.size + object.size > CAPACITY {
            let oldest = state
                .entries
                .iter()
                .min_by_key(|(_, entry)| entry.used)
                .map(|(key, _)| key.clone())
                .unwrap();
            let old = state.entries.remove(&oldest).unwrap();
            state.size -= old.size;
        }
        state.used = state.used.wrapping_add(1);
        entry.used = state.used;
        state.size += object.size;
        state.entries.insert(object.digest.clone(), entry);
        Ok(file)
    }
}
impl FileStore {
    fn download_into(
        &self,
        config: &Configuration,
        object: &FileObject,
        file: &mut fs::File,
    ) -> Result<()> {
        let actual = self.target(config)?.download(
            &object.digest,
            &mut self.space.writer(file)?,
            object.size,
        )?;
        if actual != (object.size, object.digest.clone()) {
            return Err(unavailable("文件对象完整性校验失败。"));
        }
        Ok(())
    }
    pub(super) fn download_uncached(
        &self,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<fs::File> {
        let mut file =
            paths::anonymous_file(&self.cache.join("FileDownloads")).map_err(unavailable)?;
        self.download_into(config, object, &mut file)?;
        file.seek(SeekFrom::Start(0))?;
        Ok(file)
    }
    pub(super) fn verify_object(&self, object: &FileObject) -> Result<()> {
        let config = self.configuration()?;
        match config.backend {
            Backend::Local { .. } => {
                self.open_object(object)?;
            }
            Backend::S3 { .. } => {
                let actual = self.target(&config)?.download(
                    &object.digest,
                    &mut std::io::sink(),
                    object.size,
                )?;
                if actual != (object.size, object.digest.clone()) {
                    return Err(unavailable("文件对象完整性校验失败。"));
                }
            }
        }
        Ok(())
    }
}
