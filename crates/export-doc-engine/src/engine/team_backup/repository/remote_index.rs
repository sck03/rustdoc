//! Immutable names are cached per protected handle; reader markers stay live.
use super::names::{Names, Namespace};
use super::*;
#[derive(Default)]
pub(super) struct RemoteNames(Names);
impl RemoteNames {
    pub(super) fn changed(&self, name: &str, present: bool) -> Result<()> {
        if name.starts_with("reader-") {
            return Ok(());
        }
        self.0.changed(name, present)
    }
    pub(super) fn list(
        &self,
        target: &Target,
        prefix: &str,
        after: &str,
        limit: usize,
    ) -> Result<Vec<String>> {
        if prefix.starts_with("reader-") {
            return target.list(prefix, after, limit);
        }
        #[cfg(feature = "s3-backup")]
        if matches!(target, Target::S3(_)) {
            return target.list(prefix, after, limit);
        }
        let mut names = self.0.list(prefix, after, limit, |space| {
            let names = match target {
                Target::WebDav(target) => target.object_names(space.directory())?,
                #[cfg(feature = "s3-backup")]
                Target::S3(_) => unreachable!(),
            };
            if names.iter().any(|name| {
                name.starts_with("reader-") || Namespace::of(name) as usize != space as usize
            }) {
                return Err(invalid("恢复仓库对象布局不符，请使用当前格式的新仓库。"));
            }
            Ok(names)
        })?;
        if prefix.is_empty() {
            names.extend(target.list("reader-", after, limit)?);
            names.sort();
            names.truncate(limit);
        }
        Ok(names)
    }
}
