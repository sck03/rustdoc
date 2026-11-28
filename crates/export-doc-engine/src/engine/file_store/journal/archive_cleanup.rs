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
        let intent: Intent = serde_json::from_reader(fs::File::open(path)?.take(4096))?;
        if !intent.archive_source {
            return Err(invalid("此记录不是归档副本清理任务。"));
        }
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
        fs::remove_file(path)?;
        Ok(())
    }

    pub(in crate::engine::file_store) fn reconcile_archive_sources(
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
        for token in tokens {
            crate::operation::check()?;
            if token.len() != 32 || !token.bytes().all(|byte| byte.is_ascii_hexdigit()) {
                return Err(invalid("归档副本清理编号无效。"));
            }
            let path = self.journal_root().join(format!("{token}.json"));
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            if path.try_exists()? {
                self.finish_archive_cleanup(store, &path, progress)?;
            }
        }
        Ok(())
    }

    pub(in crate::engine::file_store) fn archive_cleanup_candidates(
        &self,
        store: &Store,
    ) -> Result<Vec<(String, u64)>> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        if !root.try_exists()? {
            return Ok(Vec::new());
        }
        let mut candidates = Vec::new();
        for entry in fs::read_dir(root)?.take(1000) {
            crate::operation::check()?;
            let path = entry?.path();
            paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            if path.extension().is_none_or(|extension| extension != "json") {
                continue;
            }
            let intent: Intent = serde_json::from_reader(fs::File::open(&path)?.take(4096))?;
            if !intent.archive_source {
                continue;
            }
            let token = path
                .file_stem()
                .and_then(|value| value.to_str())
                .unwrap_or("");
            if self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件操作状态异常。"))?
                .uploads
                .contains_key(token)
            {
                continue;
            }
            validate(&intent.object)?;
            let current = store.connection()?.file_object(&intent.object.digest)?;
            let target = current
                .as_ref()
                .map(|object| self.object_configuration(object))
                .transpose()?;
            let bytes = if target.is_some_and(|target| target.identity != intent.identity) {
                intent.object.size
            } else {
                0
            };
            candidates.push((token.to_owned(), bytes));
        }
        Ok(candidates)
    }
}
