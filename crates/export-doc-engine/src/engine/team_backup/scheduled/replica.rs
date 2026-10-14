use super::*;
use sha2::{Digest, Sha256};

pub(super) enum Target {
    None,
    WebDav(cloud::RecoveryTarget),
    #[cfg(feature = "s3-backup")]
    S3(super::super::s3::Target),
}
impl Target {
    pub(super) fn read(
        store: &Store,
        protector: &crate::secrets::Protector,
        saved: &SavedPlan,
    ) -> Result<Self> {
        match saved.plan.replica_mode.as_str() {
            "None" => Ok(Self::None),
            "WebDav" => {
                cloud::RecoveryTarget::read(store, protector, &saved.repository).map(Self::WebDav)
            }
            "S3" => {
                #[cfg(feature = "s3-backup")]
                {
                    let secret = protector
                        .unprotect(S3_SECRET, &saved.s3_secret)
                        .map_err(unavailable)?;
                    let token = protector
                        .unprotect(S3_TOKEN, &saved.s3_token)
                        .map_err(unavailable)?;
                    super::super::s3::Target::new(
                        saved.plan.s3.clone(),
                        &saved.repository,
                        &secret,
                        &token,
                    )
                    .map(Self::S3)
                }
                #[cfg(not(feature = "s3-backup"))]
                Err(unsupported("此构建未包含 S3 备份模块。"))
            }
            _ => Err(invalid("副本目标无效。")),
        }
    }
    pub(super) fn identity(&self) -> String {
        let value = match self {
            Self::None => return String::new(),
            Self::WebDav(target) => target.identity(),
            #[cfg(feature = "s3-backup")]
            Self::S3(target) => target.identity(),
        };
        hex(&Sha256::digest(value.as_bytes()))
    }
    pub(super) fn upload(
        &self,
        path: &Path,
        name: &str,
        size: u64,
        digest: &str,
        verify: bool,
    ) -> Result<()> {
        match self {
            Self::None => Ok(()),
            Self::WebDav(target) => target.upload(path, name, size, digest, verify),
            #[cfg(feature = "s3-backup")]
            Self::S3(target) => target.upload(path, name, size, digest, verify),
        }
    }
    pub(super) fn delete(&self, name: &str) -> Result<()> {
        match self {
            Self::None => Ok(()),
            Self::WebDav(target) => target.delete(name),
            #[cfg(feature = "s3-backup")]
            Self::S3(target) => target.delete(name),
        }
    }
}
