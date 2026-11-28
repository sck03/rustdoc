//! One advancing, bounded journal scan and one batched catalog projection per preview.
use super::*;
use crate::engine::store::Store;

#[derive(Default)]
pub(in crate::engine::file_store) struct Discovery {
    pub orphans: Vec<FileObject>,
    pub copies: Vec<(String, u64)>,
    pub uploads: Vec<(String, String)>,
}

impl FileStore {
    pub(in crate::engine::file_store) fn intent_path(&self, token: &str) -> Result<PathBuf> {
        if token.len() != 32 || !token.bytes().all(|byte| byte.is_ascii_hexdigit()) {
            return Err(invalid("文件操作记录编号无效。"));
        }
        let path = self.journal_root().join(format!("{token}.json"));
        paths::ensure_safe_absolute(&path).map_err(unavailable)?;
        Ok(path)
    }

    /// ReadDir is retained until exhaustion, so a long-lived first page cannot
    /// starve later receipts. A restart starts a new sweep; memory stays bounded.
    fn journal_page(&self) -> Result<Vec<PathBuf>> {
        let root = self.journal_root();
        paths::ensure_safe_absolute(&root).map_err(unavailable)?;
        crate::secrets::private_directory(&root).map_err(unavailable)?;
        let mut active = self
            .uploads
            .lock()
            .map_err(|_| unavailable("文件操作状态异常。"))?;
        if active.scan.is_none() {
            active.scan = Some(fs::read_dir(root)?);
        }
        let mut paths = Vec::new();
        for _ in 0..1000 {
            crate::operation::check()?;
            match active.scan.as_mut().unwrap().next() {
                Some(entry) => paths.push(entry?.path()),
                None => {
                    active.scan = None;
                    break;
                }
            }
        }
        Ok(paths)
    }

    pub(in crate::engine::file_store) fn discover_intents(
        &self,
        store: &Store,
        config: &Configuration,
    ) -> Result<Discovery> {
        let mut entries = Vec::new();
        for path in self.journal_page()? {
            crate::operation::check()?;
            if path.extension().is_none_or(|extension| extension != "json") {
                continue;
            }
            let token = path
                .file_stem()
                .and_then(|value| value.to_str())
                .unwrap_or("")
                .to_owned();
            self.intent_path(&token)?;
            if self
                .uploads
                .lock()
                .map_err(|_| unavailable("文件操作状态异常。"))?
                .uploads
                .contains_key(&token)
            {
                continue;
            }
            if let Some(intent) = read_intent(&path)? {
                entries.push((token, path, intent));
            }
        }
        if entries.is_empty() {
            return Ok(Discovery::default());
        }
        let digests = entries
            .iter()
            .map(|(_, _, intent)| intent.object.digest.clone())
            .collect::<BTreeSet<_>>()
            .into_iter()
            .collect::<Vec<_>>();
        let states = store
            .connection()?
            .file_object_states(&digests)?
            .into_iter()
            .map(|state| (state.object.digest.clone(), state))
            .collect::<BTreeMap<_, _>>();
        let mut result = Discovery::default();
        let mut orphans = BTreeMap::new();
        let mut copies = BTreeSet::new();
        for (token, path, intent) in entries {
            let state = states.get(&intent.object.digest);
            let target = state
                .map(|state| self.object_configuration(&state.object))
                .transpose()?;
            let duplicate = target.is_some_and(|target| target.identity != intent.identity);
            if intent.copy_cleanup || (intent.identity == config.identity && duplicate) {
                if duplicate {
                    let source = self.object_configuration(&intent.object)?;
                    if source.identity == intent.identity
                        && matches!(source.backend, Backend::Local { .. })
                    {
                        self.check_identity(&source)?;
                        if !self.object_path(&source, &intent.object)?.try_exists()? {
                            remove_intent(&path)?;
                            continue;
                        }
                    }
                }
                let bytes = if duplicate && copies.insert((intent.identity, intent.object.digest)) {
                    intent.object.size
                } else {
                    0
                };
                result.copies.push((token, bytes));
            } else if intent.identity != config.identity
                || state.is_some_and(|state| state.references > 0)
            {
                // Completed uploads and receipts from a completed production migration.
                remove_intent(&path)?;
            } else {
                result.uploads.push((token, intent.object.digest.clone()));
                orphans.insert(intent.object.digest.clone(), intent.object);
            }
        }
        result.orphans = orphans.into_values().collect();
        Ok(result)
    }
}

pub(super) fn read_intent(path: &Path) -> Result<Option<Intent>> {
    paths::ensure_safe_absolute(path).map_err(unavailable)?;
    let file = match fs::File::open(path) {
        Ok(file) => file,
        Err(cause) if cause.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(cause) => return Err(cause.into()),
    };
    let intent: Intent = serde_json::from_reader(file.take(4096))
        .map_err(|_| invalid("文件操作记录损坏，请核对文件存储维护记录。"))?;
    validate(&intent.object)?;
    Ok(Some(intent))
}
