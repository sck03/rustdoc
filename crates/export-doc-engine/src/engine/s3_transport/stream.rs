//! A bounded push adapter over the shared multipart transport. Restore never
//! needs to spool an entire attachment on the application volume.
use super::*;
use std::io::Write;
const WINDOW: usize = 8 * 1024 * 1024;

pub(in crate::engine) struct StreamUpload {
    target: Target,
    session: url::Url,
    journal: Option<std::path::PathBuf>,
    parts: Vec<String>,
    buffer: Vec<u8>,
    expected: u64,
    received: u64,
    digest: String,
    hash: Sha256,
    completed: bool,
}
impl StreamUpload {
    pub(in crate::engine) fn new(
        target: Target,
        name: &str,
        size: u64,
        digest: &str,
    ) -> Result<Self> {
        if size == 0 || size.div_ceil(WINDOW as u64) > 10_000 {
            return Err(invalid(
                "流式 S3 恢复单文件须为 1 字节至 78 GiB；请拆分超出预算的文件。",
            ));
        }
        let mut initiate = target.url(name)?;
        initiate.query_pairs_mut().append_pair("uploads", "");
        let response = target.control(&target.agent, "POST", &initiate, &[])?;
        let id = xml_value(&response, "UploadId")?;
        if id.is_empty() || id.len() > 4096 {
            return Err(unavailable("S3 分段上传编号无效。"));
        }
        let mut session = target.url(name)?;
        session.query_pairs_mut().append_pair("uploadId", &id);
        let journal = match target.remember_upload(name, &id) {
            Ok(path) => path,
            Err(cause) => {
                target.abort_upload(&session)?;
                return Err(cause);
            }
        };
        Ok(Self {
            target,
            session,
            journal,
            parts: Vec::new(),
            buffer: Vec::with_capacity(WINDOW),
            expected: size,
            received: 0,
            digest: digest.into(),
            hash: Sha256::new(),
            completed: false,
        })
    }
    fn flush_part(&mut self) -> Result<()> {
        if self.buffer.is_empty() {
            return Ok(());
        }
        let mut url = self.session.clone();
        url.query_pairs_mut()
            .append_pair("partNumber", &(self.parts.len() + 1).to_string());
        let hash = hex(&Sha256::digest(&self.buffer));
        let etag = self.target.upload_part(
            &url,
            &hash,
            self.buffer.len() as u64,
            &mut std::io::Cursor::new(self.buffer.as_slice()),
        )?;
        self.parts.push(etag);
        self.buffer.clear();
        Ok(())
    }
    pub(in crate::engine) fn finish(mut self) -> Result<()> {
        if self.received != self.expected || hex(&self.hash.clone().finalize()) != self.digest {
            return Err(invalid("恢复附件长度或摘要不匹配，S3 上传已中止。"));
        }
        self.flush_part()?;
        self.target.complete_upload(&self.session, &self.parts)?;
        // Do not trust multipart ETag. The caller subsequently reads the object
        // through FileStore, which checks both digest and length before activation.
        self.completed = true;
        if let Some(path) = self.journal.take() {
            std::fs::remove_file(path)?;
        }
        Ok(())
    }
}
impl Write for StreamUpload {
    fn write(&mut self, mut bytes: &[u8]) -> std::io::Result<usize> {
        let count = bytes.len();
        if self.received.saturating_add(count as u64) > self.expected {
            return Err(std::io::Error::other("恢复附件超过声明大小。"));
        }
        while !bytes.is_empty() {
            crate::operation::check().map_err(|e| std::io::Error::other(e.message))?;
            let count = (WINDOW - self.buffer.len()).min(bytes.len());
            self.buffer.extend_from_slice(&bytes[..count]);
            self.hash.update(&bytes[..count]);
            self.received += count as u64;
            bytes = &bytes[count..];
            if self.buffer.len() == WINDOW {
                self.flush_part()
                    .map_err(|e| std::io::Error::other(e.message))?;
            }
        }
        Ok(count)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}
impl Drop for StreamUpload {
    fn drop(&mut self) {
        if !self.completed && self.target.abort_upload(&self.session).is_ok() {
            if let Some(path) = self.journal.take() {
                let _ = std::fs::remove_file(path);
            }
        }
    }
}
