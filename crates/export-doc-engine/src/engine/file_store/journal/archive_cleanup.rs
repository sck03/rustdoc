//! Reuses the upload journal and per-object exclusion for post-commit cleanup.
use super::*;
use crate::engine::store::Store;

impl FileStore {
    /// Caller holds maintenance/backup locks and the archive access write lock.
    pub(in crate::engine::file_store) fn finish_archive_cleanup(
        &self,
        store: &Store,
        path: &Path,
        progress: &mut super::super::CollectionProgress,
    ) -> Result<()> {
        progress.stage = "归档旧副本核验与删除";
        paths::ensure_safe_absolute(path).map_err(unavailable)?;
        let Some(intent) = discovery::read_intent(path)? else {
            return Ok(());
        };
        validate(&intent.object)?;
        let token = path
            .file_stem()
            .and_then(|value| value.to_str())
            .unwrap_or("");
        let key = (intent.identity.clone(), intent.object.digest.clone());
        let _collection = {
            let mut active = self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件操作状态异常。"))?;
            if active.collecting.contains(&key)
                || active
                    .uploads
                    .iter()
                    .any(|(id, value)| id != token && value == &key)
            {
                return Err(crate::engine::error::error(
                    429,
                    "同一文件仍在上传或清理，旧副本保留，请稍后重新预检回收。",
                ));
            }
            active.collecting.insert(key.clone());
            CollectionLease {
                key,
                active: self.uploads.clone(),
            }
        };
        let current = store.connection()?.file_object(&intent.object.digest)?;
        let Some(current) = current else {
            // The receipt is retained if the authoritative route no longer exists.
            return Err(unavailable(
                "归档副本的正式位置记录已不存在，请核对文件库维护记录。",
            ));
        };
        let source = self.object_configuration(&intent.object)?;
        if source.identity != intent.identity {
            return Err(unavailable(
                "待清理副本的文件库身份已改变，请接回原文件库后重试维护。",
            ));
        }
        let target = self.object_configuration(&current)?;
        if target.identity != source.identity {
            // Reverify the authoritative copy before removing the old source.
            self.verify_object(&current)?;
            self.remove_object(&source, &intent.object)?;
            progress.removed(intent.object.size);
        }; // SQL did not switch: the source stays authoritative.
        progress.stage = "归档旧副本记录清理";
        remove_intent(path)?;
        Ok(())
    }

    pub(in crate::engine::file_store) fn reconcile_copies(
        &self,
        store: &Store,
        tokens: &[String],
        progress: &mut super::super::CollectionProgress,
    ) -> Result<()> {
        if tokens.len() > 1000 {
            return Err(invalid("归档副本清理清单超过预算。"));
        }
        let _access = self
            .archive_access
            .write(std::time::Duration::from_secs(30))?;
        let mut completed = BTreeSet::new();
        for token in tokens {
            crate::operation::check()?;
            let path = self.intent_path(token)?;
            let Some(intent) = discovery::read_intent(&path)? else {
                continue;
            };
            let key = (intent.identity, intent.object.digest);
            if completed.contains(&key) {
                remove_intent(&path)?;
            } else {
                self.finish_archive_cleanup(store, &path, progress)?;
                completed.insert(key);
            }
        }
        Ok(())
    }
}
