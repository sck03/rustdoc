//! Durable upload intents bridge immutable object publication and SQL commit.
//! An interrupted upload remains observable until the administrator reconciles it.
use super::*;
mod archive_cleanup;
#[cfg(test)]
#[path = "tests/journal.rs"]
mod tests;
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::{Arc, Mutex},
};
pub(super) type Uploads = Arc<Mutex<Activity>>;
#[derive(Default)]
pub(super) struct Activity {
    uploads: BTreeMap<String, (String, String)>,
    collecting: BTreeSet<(String, String)>,
}
struct CollectionLease {
    key: (String, String),
    active: Uploads,
}
impl Drop for CollectionLease {
    fn drop(&mut self) {
        if let Ok(mut active) = self.active.lock() {
            active.collecting.remove(&self.key);
        }
    }
}
pub(super) struct UploadLease {
    pub(super) path: PathBuf,
    token: String,
    active: Uploads,
}
impl Drop for UploadLease {
    fn drop(&mut self) {
        if let Ok(mut active) = self.active.lock() {
            active.uploads.remove(&self.token);
        }
    }
}
#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Intent {
    identity: String,
    object: FileObject,
    archive_source: bool,
}
impl FileStore {
    fn journal_root(&self) -> PathBuf {
        self.configuration_path
            .parent()
            .unwrap()
            .join("FileUploads")
    }
    pub(super) fn begin_upload(
        &self,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<UploadLease> {
        self.begin_intent(config, object, false)
    }
    pub(super) fn begin_archive_cleanup(
        &self,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<UploadLease> {
        self.begin_intent(config, object, true)
    }
    fn begin_intent(
        &self,
        config: &Configuration,
        object: &FileObject,
        archive_source: bool,
    ) -> Result<UploadLease> {
        let token = paths::nonce().map_err(unavailable)?;
        let lease = UploadLease {
            path: self.journal_root().join(format!("{token}.json")),
            token: token.clone(),
            active: self.uploads.clone(),
        };
        {
            let mut active = self.uploads.lock().map_err(|_| {
                unavailable("文件上传状态异常。")
                    .coded(crate::api::ErrorCode::DependencyUnavailable)
            })?;
            let key = (config.identity.clone(), object.digest.clone());
            if active.collecting.contains(&key) {
                return Err(super::super::error::error(
                    429,
                    "相同文件正在回收，请稍后重试上传。",
                ));
            }
            active.uploads.insert(token.clone(), key);
        }
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        paths::atomic_write(
            &root.join(format!("{token}.json")),
            &serde_json::to_vec(&Intent {
                identity: config.identity.clone(),
                object: object.clone(),
                archive_source,
            })?,
        )
        .map_err(unavailable)?;
        let mark = self
            .configuration_path
            .parent()
            .unwrap()
            .join("FileCollection")
            .join(format!("{}-{}.json", config.identity, object.digest));
        paths::ensure_safe_absolute(&mark).map_err(unavailable)?;
        if mark.try_exists()? {
            fs::remove_file(mark)?;
        }
        Ok(lease)
    }
    /// Bounded discovery of abandoned uploads, including objects without a SQL row.
    pub(super) fn orphan_intents(
        &self,
        store: &super::super::store::Store,
        config: &Configuration,
    ) -> Result<Vec<FileObject>> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        let mut objects = BTreeMap::new();
        for entry in fs::read_dir(root)?.take(1000) {
            crate::operation::check()?;
            let path = entry?.path();
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            if path.extension().is_none_or(|e| e != "json") {
                continue;
            }
            let file = match fs::File::open(&path) {
                Ok(file) => file,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                Err(e) => return Err(e.into()),
            };
            let intent: Intent = serde_json::from_reader(file.take(4096))
                .map_err(|_| invalid("文件上传回收记录损坏，请核对文件存储维护记录。"))?;
            validate(&intent.object)?;
            if intent.archive_source {
                continue;
            }

            let token = path.file_stem().and_then(|v| v.to_str()).unwrap_or("");
            let active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件上传状态异常。"))?;
            if active.uploads.contains_key(token) {
                continue;
            }
            drop(active);
            if intent.identity != config.identity {
                fs::remove_file(&path)?;
                continue;
            }
            if store
                .connection()?
                .file_is_referenced(&intent.object.digest)?
            {
                match fs::remove_file(&path) {
                    Ok(()) => {}
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
                    Err(e) => return Err(e.into()),
                }
            } else {
                objects.insert(intent.object.digest.clone(), intent.object);
                if objects.len() == 500 {
                    break;
                }
            }
        }
        Ok(objects.into_values().collect())
    }
    pub(super) fn finish_collection(
        &self,
        config: &Configuration,
        removed: &BTreeSet<String>,
    ) -> Result<()> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        if root.try_exists()? {
            for entry in fs::read_dir(root)?.take(1000) {
                crate::operation::check()?;
                let path = entry?.path();
                paths::ensure_safe_absolute(&path).map_err(unavailable)?;
                if path.extension().is_none_or(|e| e != "json") {
                    continue;
                }
                let file = match fs::File::open(&path) {
                    Ok(f) => f,
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                    Err(e) => return Err(e.into()),
                };
                let intent: Intent = serde_json::from_reader(file.take(4096))?;
                let active = self
                    .uploads
                    .lock()
                    .map_err(|_| unavailable("文件上传状态异常。"))?;
                if !intent.archive_source
                    && intent.identity == config.identity
                    && removed.contains(&intent.object.digest)
                    && !active
                        .uploads
                        .contains_key(path.file_stem().and_then(|v| v.to_str()).unwrap_or(""))
                {
                    match fs::remove_file(&path) {
                        Ok(()) => {}
                        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
                        Err(e) => return Err(e.into()),
                    }
                }
            }
        }
        #[cfg(feature = "s3-storage")]
        if matches!(config.backend, Backend::S3 { .. }) {
            self.target(config)?.recover_uploads(|name| {
                Ok(self
                    .uploads
                    .lock()
                    .map_err(|_| unavailable("文件上传状态异常。"))?
                    .uploads
                    .values()
                    .any(|(id, digest)| id == &config.identity && digest == name))
            })?;
        }
        Ok(())
    }
    /// Caller holds the backup/migration locks. Only reference checks and metadata
    /// writes use the application gate; a per-object lease excludes new uploads.
    /// Intents and unreferenced SQL rows survive an interrupted physical delete.
    #[cfg(test)]
    pub(super) fn reconcile(&self, store: &super::super::store::Store) -> Result<u64> {
        let config = self.configuration()?;
        let mut objects = self.orphan_intents(store, &config)?;
        objects.extend(store.connection()?.file_objects("", 1000, true)?);
        let mut removed = BTreeSet::new();
        let mut reclaimed = 0;
        for object in objects {
            if !removed.contains(&object.digest) && self.collect(store, &config, &object)? {
                reclaimed += object.size;
                removed.insert(object.digest);
            }
        }
        self.finish_collection(&config, &removed)?;
        Ok(reclaimed)
    }
    fn claim_collection(
        &self,
        store: &super::super::store::Store,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<Option<CollectionLease>> {
        store.with_writes_paused(|| {
            let mut active = self.uploads.lock().map_err(|_| {
                unavailable("文件上传状态异常。")
                    .coded(crate::api::ErrorCode::DependencyUnavailable)
            })?;
            let key = (config.identity.clone(), object.digest.clone());
            if active.uploads.values().any(|value| value == &key)
                || active.collecting.contains(&key)
                || store.connection()?.file_is_referenced(&object.digest)?
            {
                return Ok(None);
            }
            active.collecting.insert(key.clone());
            Ok(Some(CollectionLease {
                key,
                active: self.uploads.clone(),
            }))
        })
    }
    pub(super) fn collect(
        &self,
        store: &super::super::store::Store,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<bool> {
        let Some(_lease) = self.claim_collection(store, config, object)? else {
            return Ok(false);
        };
        self.remove_object(&self.object_configuration(object)?, object)?;
        store.transaction(|tx| {
            tx.remove_unused_file(&object.digest)?;
            Ok(())
        })?;
        Ok(true)
    }
    pub(super) fn remove_object(&self, config: &Configuration, object: &FileObject) -> Result<()> {
        match &config.backend {
            Backend::Local { .. } => {
                self.local_identity(config)?;
                match fs::remove_file(self.object_path(config, object)?) {
                    Ok(()) => Ok(()),
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
                    Err(e) => Err(e.into()),
                }
            }
            Backend::S3 { .. } => {
                self.health()?;
                self.target(config)?.delete(&object.digest)
            }
        }
    }
}
