//! Physical data objects are separate from the small live repository catalog.
//! Cached names are scoped to one protected repository handle, never a TTL.
use super::*;
use std::{collections::BTreeSet, sync::Mutex};
#[derive(Clone, Copy)]
pub(in crate::engine::team_backup) enum Namespace {
    Metadata,
    Objects,
}
impl Namespace {
    pub(in crate::engine::team_backup) fn of(name: &str) -> Self {
        if name.starts_with("blob-") || name.starts_with("manifest-") {
            Self::Objects
        } else {
            Self::Metadata
        }
    }
    pub(in crate::engine::team_backup) fn directory(self) -> &'static str {
        match self {
            Self::Metadata => "",
            Self::Objects => "objects",
        }
    }
    fn matching(prefix: &str) -> &'static [Self] {
        if prefix.starts_with("blob-") || prefix.starts_with("manifest-") {
            &[Self::Objects]
        } else if "blob-".starts_with(prefix) || "manifest-".starts_with(prefix) {
            &[Self::Metadata, Self::Objects]
        } else {
            &[Self::Metadata]
        }
    }
}
#[derive(Default)]
pub(super) struct Names(Mutex<[Option<BTreeSet<String>>; 2]>);
impl Names {
    pub(super) fn changed(&self, name: &str, present: bool) -> Result<()> {
        let mut indexes = self
            .0
            .lock()
            .map_err(|_| unavailable("仓库目录索引不可用。"))?;
        let index = &mut indexes[Namespace::of(name) as usize];
        if let Some(names) = index {
            if present {
                names.insert(name.to_owned());
            } else {
                names.remove(name);
            }
            if names.len() > 2_000_000 {
                *index = None;
            }
        }
        Ok(())
    }
    pub(super) fn list(
        &self,
        prefix: &str,
        after: &str,
        limit: usize,
        mut load: impl FnMut(Namespace) -> Result<Vec<String>>,
    ) -> Result<Vec<String>> {
        let mut indexes = self
            .0
            .lock()
            .map_err(|_| unavailable("仓库目录索引不可用。"))?;
        let mut result = BTreeSet::new();
        for &space in Namespace::matching(prefix) {
            let index = &mut indexes[space as usize];
            if index.is_none() {
                let names = load(space)?;
                for name in &names {
                    io::valid_name(name)?;
                }
                if names.len() > 2_000_000 {
                    return Err(invalid("仓库目录索引超过两百万对象预算，请分库管理。"));
                }
                *index = Some(names.into_iter().collect());
            }
            result.extend(
                index
                    .as_ref()
                    .unwrap()
                    .range(prefix.max(after).to_owned()..)
                    .take_while(|name| name.starts_with(prefix))
                    .filter(|name| name.as_str() > after)
                    .take(limit)
                    .cloned(),
            );
        }
        Ok(result.into_iter().take(limit).collect())
    }
}
