#[cfg(feature = "s3-storage")]
pub(super) use crate::engine::s3_transport::Target;

// A local-only build keeps the same persisted configuration contract and fails
// explicitly when asked to open remote storage. It never falls back to disk.
#[cfg(not(feature = "s3-storage"))]
pub(super) struct Target;
#[cfg(not(feature = "s3-storage"))]
impl Target {
    pub fn new(
        _: export_doc_domain::backup_plan::S3Settings,
        _: &str,
        _: &str,
        _: &str,
    ) -> super::Result<Self> {
        Err(crate::engine::error::unsupported(
            "当前构建未启用 S3 文件存储。",
        ))
    }
    pub fn upload_file(
        &self,
        _: &mut std::fs::File,
        _: &str,
        _: u64,
        _: &str,
    ) -> super::Result<()> {
        Err(crate::engine::error::unsupported(
            "当前构建未启用 S3 文件存储。",
        ))
    }
    pub fn download(&self, _: &str, _: &mut impl std::io::Write, _: u64) -> super::Result<()> {
        Err(crate::engine::error::unsupported(
            "当前构建未启用 S3 文件存储。",
        ))
    }
    pub fn delete(&self, _: &str) -> super::Result<()> {
        Err(crate::engine::error::unsupported(
            "当前构建未启用 S3 文件存储。",
        ))
    }
}
