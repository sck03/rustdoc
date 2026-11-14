use super::*;
use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf};

#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Session {
    name: String,
    upload_id: String,
}

impl Target {
    pub(in crate::engine) fn with_journal(mut self, root: PathBuf) -> Self {
        self.journal = Some(root.join(crate::engine::media::digest(self.identity().as_bytes())));
        self
    }
    pub(super) fn remember_upload(&self, name: &str, upload_id: &str) -> Result<Option<PathBuf>> {
        let Some(root) = &self.journal else {
            return Ok(None);
        };
        crate::paths::ensure_safe_absolute(root).map_err(unavailable)?;
        crate::secrets::private_directory(root).map_err(unavailable)?;
        let path = root.join(format!(
            "{}.json",
            crate::paths::nonce().map_err(unavailable)?
        ));
        crate::paths::atomic_write(
            &path,
            &serde_json::to_vec(&Session {
                name: name.into(),
                upload_id: upload_id.into(),
            })?,
        )
        .map_err(unavailable)?;
        Ok(Some(path))
    }
    pub(super) fn abort_upload(&self, session: &url::Url) -> Result<()> {
        crate::operation::OperationScope::detached(CONTROL_TIMEOUT).run(|| {
            let agent = export_doc_network::storage_agent(
                CONTROL_TIMEOUT,
                crate::operation::cancellation_flag(),
            );
            let request = self.request("DELETE", session, (), None, EMPTY_HASH)?;
            let response = agent
                .run(request)
                .map_err(|_| unavailable("S3 中止分段上传失败。"))?;
            if response.status().as_u16() == 404 {
                return Ok(());
            }
            response_text(response).map(|_| ())
        })
    }
    pub(in crate::engine) fn recover_uploads(
        &self,
        mut active: impl FnMut(&str) -> Result<bool>,
    ) -> Result<()> {
        let Some(root) = &self.journal else {
            return Ok(());
        };
        crate::paths::ensure_safe_absolute(root).map_err(unavailable)?;
        if !root.try_exists()? {
            return Ok(());
        }
        for entry in fs::read_dir(root)? {
            crate::operation::check()?;
            let path = entry?.path();
            crate::paths::ensure_safe_absolute(&path).map_err(unavailable)?;
            if path.extension().is_none_or(|extension| extension != "json") {
                continue;
            }
            let record: Session = serde_json::from_reader(fs::File::open(&path)?.take(8192))
                .map_err(|_| unavailable("S3 分片恢复记录损坏。"))?;
            if record.upload_id.is_empty() || record.upload_id.len() > 4096 {
                return Err(unavailable("S3 分片恢复编号无效。"));
            }
            if active(&record.name)? {
                continue;
            }
            let mut session = self.url(&record.name)?;
            session
                .query_pairs_mut()
                .append_pair("uploadId", &record.upload_id);
            self.abort_upload(&session)?;
            fs::remove_file(path)?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn interrupted_multipart_is_recovered_but_active_sessions_are_preserved() {
        let root = crate::engine::test_support::ScratchDirectory::new();
        let fixture = crate::engine::test_support::replica_fixture::Fixture::start(
            root.0.clone(),
            true,
            false,
            None,
        );
        let settings = S3Settings {
            endpoint: format!("http://{}", fixture.address),
            bucket: "fixture-bucket".into(),
            access_key_id: "fixture-access".into(),
            addressing: S3Addressing::Path,
            ..Default::default()
        };
        let target = Target::new(settings, "repository", "secret", "")
            .unwrap()
            .with_journal(root.0.join("uploads"));
        let path = target
            .remember_upload("object", "interrupted&id")
            .unwrap()
            .unwrap();
        target.recover_uploads(|_| Ok(true)).unwrap();
        assert!(path.exists());
        target.recover_uploads(|_| Ok(false)).unwrap();
        assert!(!path.exists());
        assert_eq!(fixture.finish().len(), 1);
    }
}
