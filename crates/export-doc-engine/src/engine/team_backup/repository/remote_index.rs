//! A WebDAV enumeration belongs to one protected repository session. Reader
//! markers are live; writers update their index after each successful mutation.
use super::*;
use std::{collections::BTreeSet, sync::Mutex};
#[derive(Default)]
pub(super) struct Names(Mutex<Option<BTreeSet<String>>>);
impl Names {
    pub(super) fn changed(&self, name: &str, present: bool) -> Result<()> {
        let mut snapshot = self
            .0
            .lock()
            .map_err(|_| unavailable("仓库目录索引不可用。"))?;
        if let Some(names) = snapshot.as_mut() {
            if present {
                names.insert(name.to_owned());
            } else {
                names.remove(name);
            }
            if names.len() > 1_000_000 {
                *snapshot = None;
            }
        }
        Ok(())
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
        let mut snapshot = self
            .0
            .lock()
            .map_err(|_| unavailable("仓库目录索引不可用。"))?;
        if snapshot.is_none() {
            let names = match target {
                Target::WebDav(target) => target.object_names()?,
                #[cfg(feature = "s3-backup")]
                Target::S3(_) => unreachable!(),
            };
            for name in &names {
                io::valid_name(name)?;
                if name.starts_with("reader-") {
                    return Err(invalid(
                        "恢复仓库含旧布局的读取保护，请停止旧程序并显式处理后再使用。",
                    ));
                }
            }
            *snapshot = Some(names.into_iter().collect());
        }
        let mut names: BTreeSet<_> = snapshot
            .as_ref()
            .unwrap()
            .range(prefix.max(after).to_owned()..)
            .take_while(|name| name.starts_with(prefix))
            .filter(|name| name.as_str() > after)
            .take(limit)
            .cloned()
            .collect();
        drop(snapshot);
        if prefix.is_empty() {
            names.extend(target.list("reader-", after, limit)?);
        }
        Ok(names.into_iter().take(limit).collect())
    }
}
