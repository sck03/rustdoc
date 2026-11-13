use super::*;
pub(in crate::engine) const MAX_UPLOAD_BYTES: u64 = 100 * 1024 * 1024;
pub(in crate::engine) const MAX_REQUEST_FILES: i64 = 100;

impl Policy {
    pub(super) fn validate(&self) -> Result<()> {
        if !(1..=MAX_UPLOAD_BYTES).contains(&self.file_bytes)
            || !(1..=MAX_REQUEST_FILES as u32).contains(&self.request_files)
            || self.request_bytes < self.file_bytes
            || self.request_bytes > 1024 * 1024 * 1024
            || self.quota_bytes > i64::MAX as u64
        {
            return Err(invalid(
                "单文件最多 100 MiB，每份申请最多 100 个附件、合计 1 GiB。",
            ));
        }
        Ok(())
    }
    pub fn check_upload(&self, bytes: u64) -> Result<()> {
        if bytes == 0 || bytes > self.file_bytes {
            return Err(invalid(format!(
                "文件须为 1 字节至 {} 字节。",
                self.file_bytes
            )));
        }
        Ok(())
    }
    pub fn check_request(&self, count: usize, total: u64) -> Result<()> {
        if count > self.request_files as usize || total > self.request_bytes {
            return Err(invalid(format!(
                "每份记录最多 {} 个附件、合计 {} 字节。",
                self.request_files, self.request_bytes
            )));
        }
        Ok(())
    }
}
