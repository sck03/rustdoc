//! Process-local verified objects. Independent handles serve overlapping Range
//! requests; eviction unlinks only files created by this cache instance.
use super::*;
use crate::paths::file_stamp;
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::{Arc, Mutex},
};
const CAPACITY: u64 = 256 * 1024 * 1024;
const MAX_ENTRIES: usize = 1024;
struct Entry {
    path: PathBuf,
    size: u64,
    stamp: Option<file_stamp::Stamp>,
}
impl Drop for Entry {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}
#[derive(Default)]
struct State {
    entries: BTreeMap<String, (u64, Arc<Entry>)>,
    order: BTreeSet<(u64, String)>,
    used: u64,
    size: u64,
}
impl State {
    fn remove(&mut self, key: &str) -> Option<Arc<Entry>> {
        let (used, entry) = self.entries.remove(key)?;
        self.order.remove(&(used, key.to_owned()));
        self.size -= entry.size;
        Some(entry)
    }
    fn touch(&mut self, key: &str) {
        let Some((used, _)) = self.entries.get_mut(key) else {
            return;
        };
        self.order.remove(&(*used, key.to_owned()));
        self.used = self.used.wrapping_add(1);
        *used = self.used;
        self.order.insert((*used, key.to_owned()));
    }
    fn insert(&mut self, key: String, entry: Arc<Entry>) -> Vec<Arc<Entry>> {
        let mut removed: Vec<_> = self.remove(&key).into_iter().collect();
        while self.size + entry.size > CAPACITY || self.entries.len() >= MAX_ENTRIES {
            let oldest = self.order.first().unwrap().1.clone();
            removed.push(self.remove(&oldest).unwrap());
        }
        self.size += entry.size;
        self.entries.insert(key.clone(), (0, entry));
        self.touch(&key);
        removed
    }
}
#[derive(Default)]
pub(super) struct Cache {
    state: Mutex<State>,
    flights: super::flights::Flights,
}
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
        let entry = self
            .state
            .lock()
            .map_err(|_| unavailable("文件缓存状态异常。"))?
            .entries
            .get(&object.digest)
            .map(|(_, entry)| entry.clone());
        let Some(entry) = entry else {
            return Ok(None);
        };
        paths::ensure_safe_absolute(&entry.path).map_err(unavailable)?;
        let file = match fs::File::open(&entry.path) {
            Ok(file) => Some(file),
            Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => None,
            Err(cause) => return Err(cause.into()),
        };
        let stamp = file.as_ref().map(file_stamp::read).transpose()?;
        let mut state = self
            .state
            .lock()
            .map_err(|_| unavailable("文件缓存状态异常。"))?;
        let current = state
            .entries
            .get(&object.digest)
            .is_some_and(|(_, value)| Arc::ptr_eq(value, &entry));
        if stamp.as_ref().is_none_or(|stamp| stamp.size != object.size) || stamp != entry.stamp {
            let old = current.then(|| state.remove(&object.digest));
            drop(state);
            drop(old);
            return Ok(None);
        }
        if current {
            state.touch(&object.digest);
        }
        drop(state);
        Ok(file)
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
        let flight = self.flights.get(&object.digest)?;
        let _filling = flight.write(std::time::Duration::from_secs(
            super::super::backup_io::TIMEOUT_SECONDS,
        ))?;
        if let Some(file) = self.get(object)? {
            return Ok(file);
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
            stamp: None,
        };
        store.download_into(config, object, &mut file)?;
        entry.stamp = Some(file_stamp::read(&file)?);
        file.seek(SeekFrom::Start(0))?;
        let mut state = self
            .state
            .lock()
            .map_err(|_| unavailable("文件缓存状态异常。"))?;
        let removed = state.insert(object.digest.clone(), Arc::new(entry));
        drop(state);
        // Existing readers retain their own entry until open/verification finishes.
        drop(removed);
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
                self.local_identity(&config)?;
                verify(
                    &mut fs::File::open(self.object_path(&config, object)?)?,
                    object,
                )?;
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

#[cfg(test)]
mod tests {
    use super::*;
    fn entry(size: u64) -> Arc<Entry> {
        Arc::new(Entry {
            path: PathBuf::new(),
            size,
            stamp: None,
        })
    }
    #[test]
    fn cache_bounds_small_entries_and_evicts_in_usage_order() {
        let mut state = State::default();
        let first = entry(1);
        state.insert("first".into(), first.clone());
        for index in 0..MAX_ENTRIES - 1 {
            state.insert(index.to_string(), entry(1));
        }
        state.touch("first");
        let removed = state.insert("next".into(), entry(1));
        assert_eq!(removed.len(), 1);
        assert!(!state.entries.contains_key("0"));
        assert!(state.entries.contains_key("first"));
        assert_eq!(state.entries.len(), MAX_ENTRIES);
        assert_eq!(state.order.len(), MAX_ENTRIES);
        let removed = state.insert("large".into(), entry(CAPACITY));
        assert_eq!(removed.len(), MAX_ENTRIES);
        assert_eq!(state.entries.len(), 1);
        assert_eq!(state.order.len(), 1);
        assert_eq!(state.size, CAPACITY);
        assert_eq!(
            Arc::strong_count(&first),
            2,
            "evicted file stays owned until deletion outside the state lock"
        );
        drop(removed);
        assert_eq!(Arc::strong_count(&first), 1);
    }
}
