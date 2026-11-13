//! Durable upload intents bridge immutable object publication and SQL commit.
//! An interrupted upload remains observable until the administrator reconciles it.
use super::*;
use std::{
    collections::BTreeMap,
    sync::{Arc, Mutex},
};
pub(super) type Uploads = Arc<Mutex<BTreeMap<String, (String, String)>>>;
pub(super) struct UploadLease {
    token: String,
    active: Uploads,
}
impl Drop for UploadLease {
    fn drop(&mut self) {
        if let Ok(mut active) = self.active.lock() {
            active.remove(&self.token);
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
            token: token.clone(),
            active: self.uploads.clone(),
        };
        self.uploads
            .lock()
            .map_err(|_| unavailable("文件上传状态异常。"))?
            .insert(
                token.clone(),
                (config.identity.clone(), object.digest.clone()),
            );
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
    /// Caller holds the backup/migration locks and application write gate.
    /// Physical deletes finish before SQL metadata is removed. A retry is safe.
    pub(super) fn reconcile(&self, store: &super::super::store::Store) -> Result<u64> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        let config = self.configuration()?;
        let mut reclaimed = 0u64;
        for entry in fs::read_dir(&root)? {
            crate::operation::check()?;
            let path = entry?.path();
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            if path.extension().is_none_or(|e| e != "json") {
                continue;
            }
            let intent: Intent = serde_json::from_reader(fs::File::open(&path)?.take(4096))
                .map_err(|_| unavailable("文件上传回收记录损坏。"))?;
            validate(&intent.object)?;
            let active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件上传状态异常。"))?;
            if active.values().any(|(identity, digest)| {
                identity == &intent.identity && digest == &intent.object.digest
            }) {
                continue;
            }
            let referenced = store
                .connection()?
                .file_is_referenced(&intent.object.digest)?;
            if referenced {
                fs::remove_file(path)?;
                continue;
            }
            // Old locations remain rollback material after migration; never
            // infer that an object there may be deleted from today's metadata.
            if intent.identity != config.identity {
                continue;
            }
            self.remove_object(&config, &intent.object)?;
            store
                .connection()?
                .remove_unused_file(&intent.object.digest)?;
            fs::remove_file(path)?;
            reclaimed += intent.object.size;
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
                let active = self
                    .uploads
                    .lock()
                    .map_err(|_| unavailable("文件上传状态异常。"))?;
                if active
                    .values()
                    .any(|(id, digest)| id == &config.identity && digest == &object.digest)
                {
                    continue;
                }
                if store.connection()?.file_is_referenced(&object.digest)? {
                    continue;
                }
                self.remove_object(&config, &object)?;
                store.connection()?.remove_unused_file(&object.digest)?;
                reclaimed += object.size;
            }
        }
        Ok(reclaimed)
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
