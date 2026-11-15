//! Durable upload intents bridge immutable object publication and SQL commit.
//! An interrupted upload remains observable until the administrator reconciles it.
use super::*;
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
        let token = paths::nonce().map_err(unavailable)?;
        let lease = UploadLease {
            path: self.journal_root().join(format!("{token}.json")),
            token: token.clone(),
            active: self.uploads.clone(),
        };
        {
            let mut active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件上传状态异常。"))?;
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
            })?,
        )
        .map_err(unavailable)?;
        Ok(lease)
    }
    /// Caller holds the backup/migration locks. Only reference checks and metadata
    /// writes use the application gate; a per-object lease excludes new uploads.
    /// Intents and unreferenced SQL rows survive an interrupted physical delete.
    pub(super) fn reconcile(&self, store: &super::super::store::Store) -> Result<u64> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        let config = self.configuration()?;
        #[cfg(feature = "s3-storage")]
        if matches!(&config.backend, Backend::S3 { .. }) {
            self.target(&config)?.recover_uploads(|name| {
                Ok(self
                    .uploads
                    .lock()
                    .map_err(|_| unavailable("文件上传状态异常。"))?
                    .uploads
                    .values()
                    .any(|(id, digest)| id == &config.identity && digest == name))
            })?;
        }
        let mut reclaimed = 0u64;
        for entry in fs::read_dir(&root)? {
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
                .map_err(|_| unavailable("文件上传回收记录损坏。"))?;
            validate(&intent.object)?;
            // Old locations remain rollback material after migration; never
            // infer that an object there may be deleted from today's metadata.
            if intent.identity != config.identity {
                continue;
            }
            if self.collect(store, &config, &intent.object)? {
                reclaimed += intent.object.size;
            }
            // A live upload owns its intent until commit or abandonment.
            let active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件上传状态异常。"))?;
            if !active
                .uploads
                .contains_key(path.file_stem().and_then(|s| s.to_str()).unwrap_or(""))
            {
                match fs::remove_file(path) {
                    Ok(()) => {}
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
                    Err(e) => return Err(e.into()),
                }
            }
        }
        let mut cursor = String::new();
        loop {
            let objects = store.connection()?.file_objects(&cursor, 200, true)?;
            if objects.is_empty() {
                break;
            }
            for object in objects {
                crate::operation::check()?;
                cursor = object.digest.clone();
                if self.collect(store, &config, &object)? {
                    reclaimed += object.size;
                }
            }
        }
        Ok(reclaimed)
    }
    fn claim_collection(
        &self,
        store: &super::super::store::Store,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<Option<CollectionLease>> {
        store.with_writes_paused(|| {
            let mut active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件上传状态异常。"))?;
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
    fn collect(
        &self,
        store: &super::super::store::Store,
        config: &Configuration,
        object: &FileObject,
    ) -> Result<bool> {
        let Some(_lease) = self.claim_collection(store, config, object)? else {
            return Ok(false);
        };
        self.remove_object(config, object)?;
        store.transaction(|tx| {
            tx.remove_unused_file(&object.digest)?;
            Ok(())
        })?;
        Ok(true)
    }
    fn remove_object(&self, config: &Configuration, object: &FileObject) -> Result<()> {
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
