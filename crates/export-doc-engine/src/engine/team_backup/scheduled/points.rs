use super::super::repository::{
    Repository,
    io::ObjectStore,
    remote::{Remote, Target},
};
use super::*;
pub(super) fn open(
    store: &Store,
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    saved: &SavedPlan,
    password: &str,
    create: bool,
) -> Result<Repository> {
    open_with_fence(store, paths, protector, saved, password, create, false)
}
pub(super) fn open_with_fence(
    store: &Store,
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    saved: &SavedPlan,
    password: &str,
    create: bool,
    fenced: bool,
) -> Result<Repository> {
    if saved.plan.replica_mode == "None" || saved.plan.replica_mode == "Directory" {
        let base = if saved.plan.replica_mode == "Directory" {
            paths
                .file_storage_roots
                .get(&saved.plan.backup_location)
                .cloned()
                .ok_or_else(|| invalid("备份位置未登记。"))?
        } else {
            paths.data_root.join("Backups/RecoveryRepositories")
        };
        store
            .files
            .reject_repository_overlap(&base.join(format!("recovery-{}", saved.repository)))?;
    }
    let io: Box<dyn ObjectStore> = match saved.plan.replica_mode.as_str() {
        "None" | "Directory" => Box::new(super::super::repository::directory(
            paths,
            if saved.plan.replica_mode == "Directory" {
                &saved.plan.backup_location
            } else {
                ""
            },
            &saved.repository,
            (!saved.medium_identity.is_empty()).then_some(saved.medium_identity.as_str()),
            create,
        )?),
        "WebDav" => Box::new(Remote::open(
            Target::WebDav(cloud::RecoveryTarget::read(
                store,
                protector,
                &saved.repository,
            )?),
            &saved.writer_identity,
            fenced,
        )?),
        "S3" => {
            #[cfg(feature = "s3-backup")]
            {
                let secret = protector
                    .unprotect(S3_SECRET, &saved.s3_secret)
                    .map_err(unavailable)?;
                let token = protector
                    .unprotect(S3_TOKEN, &saved.s3_token)
                    .map_err(unavailable)?;
                Box::new(Remote::open(
                    Target::S3(super::super::s3::Target::new(
                        saved.plan.s3.clone(),
                        &saved.repository,
                        &secret,
                        &token,
                    )?),
                    &saved.writer_identity,
                    fenced,
                )?)
            }
            #[cfg(not(feature = "s3-backup"))]
            {
                return Err(unsupported("此构建未包含 S3 备份模块。"));
            }
        }
        _ => return Err(invalid("恢复仓库目标无效。")),
    };
    Repository::open(io, &saved.repository, password, create)
}
pub(super) fn medium(paths: &RuntimePaths, plan: &BackupPlan) -> Result<String> {
    let root = match plan.replica_mode.as_str() {
        "None" => &paths.data_root,
        "Directory" => paths
            .file_storage_roots
            .get(&plan.backup_location)
            .ok_or_else(|| invalid("备份位置尚未登记。"))?,
        _ => return Ok(String::new()),
    };
    if !root.is_dir() {
        return Err(unavailable("备份位置未挂载，请连接目标设备后保存。"));
    }
    crate::paths::volume_identity(root).map_err(unavailable)
}
pub(super) fn page(service: &NativeService, query: &[(&str, String)]) -> Result<Value> {
    let saved = load(&service.store)?;
    let number = super::super::header(query, "page")
        .parse::<usize>()
        .unwrap_or(1)
        .max(1);
    let size = super::super::header(query, "pageSize")
        .parse::<usize>()
        .unwrap_or(20)
        .clamp(1, 100);
    if saved.repository.is_empty() || saved.password.is_empty() {
        return Ok(json!({"items":[],"page":1,"pageSize":size,"totalCount":0}));
    }
    let password = service
        .protector
        .unprotect(PASSWORD, &saved.password)
        .map_err(unavailable)?;
    let _lock = package::lock(&service.paths)?;
    let repo = open(
        &service.store,
        &service.paths,
        &service.protector,
        &saved,
        &password,
        false,
    )?;
    service
        .recovery_catalog
        .page(&repo, saved.revision, number, size)
}
