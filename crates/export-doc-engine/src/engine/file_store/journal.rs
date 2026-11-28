//! Durable upload intents bridge immutable object publication and SQL commit.
//! An interrupted upload remains observable until the administrator reconciles it.
use super::*;
mod archive_cleanup;
mod discovery;
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
    scan: Option<fs::ReadDir>,
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
pub(in crate::engine) struct UploadLease {
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
    copy_cleanup: bool,
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
        copy_cleanup: bool,
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
                copy_cleanup,
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
    pub(super) fn finish_collection(
        &self,
        config: &Configuration,
        removed: &BTreeSet<String>,
        intents: &[(String, String)],
    ) -> Result<()> {
        #[cfg(not(feature = "s3-storage"))]
        let _ = config;
        for (token, digest) in intents {
            crate::operation::check()?;
            if removed.contains(digest) {
                let path = self.intent_path(token)?;
                let active = self
                    .uploads
                    .lock()
                    .map_err(|_| unavailable("文件上传状态异常。"))?;
                if !active.uploads.contains_key(token) {
                    remove_intent(&path)?;
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
        let discovered = self.discover_intents(store, &config)?;
        let mut objects = discovered.orphans;
        objects.extend(store.connection()?.file_objects("", 1000, true)?);
        let mut removed = BTreeSet::new();
        let mut progress = super::CollectionProgress::default();
        let result = (|| {
            for object in objects {
                if !removed.contains(&object.digest)
                    && self.collect(store, &config, &object, &mut progress)?
                {
                    removed.insert(object.digest);
                }
            }
            self.finish_collection(&config, &removed, &discovered.uploads)?;
            Ok(progress.bytes)
        })();
        result.map_err(|cause| progress.failure(cause))
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
        progress: &mut super::CollectionProgress,
    ) -> Result<bool> {
        progress.stage = "无引用对象删除";
        let Some(_lease) = self.claim_collection(store, config, object)? else {
            return Ok(false);
        };
        self.remove_object(&self.object_configuration(object)?, object)?;
        progress.removed(object.size);
        progress.stage = "对象元数据清理";
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
                self.target(config)?
                    .delete(&object.digest)
                    .map_err(|mut cause| {
                        // A lost delete response does not prove the object still exists.
                        cause.outcome_unknown = true;
                        cause
                    })
            }
        }
    }
}

/// A concurrent successful transaction may already have retired this receipt.
pub(super) fn remove_intent(path: &Path) -> Result<()> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(cause) => Err(cause.into()),
    }
}
