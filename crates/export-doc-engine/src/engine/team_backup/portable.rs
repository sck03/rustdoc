//! One-shot exports and daily recovery points share one repository protocol.
use super::*;
use std::fs;
pub(super) fn create(
    store: &Store,
    paths: &RuntimePaths,
    protector: &crate::secrets::Protector,
    snapshot: &Path,
    provider: &str,
    password: &str,
) -> Result<PathBuf> {
    let working = super::scratch::create(&paths.cache_root, "portable-build-")?;
    let result = (|| {
        let repo_root = working.join("repository");
        package::private_directory(&repo_root)?;
        let id = crate::paths::nonce().map_err(unavailable)?;
        let repo = repository::Repository::open(
            Box::new(repository::io::Directory::open(repo_root, None)?),
            &id,
            password,
            true,
        )?;
        let point = crate::paths::nonce().map_err(unavailable)?;
        repository::capture::create(&repo, store, paths, protector, snapshot, provider, &point)?;
        let output = paths.data_root.join("Backups/PortableRecovery");
        package::private_directory(&output)?;
        let output = output.join(format!("edm-{point}.edmpoint"));
        let mut file = fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&output)?;
        let written = repo
            .export(
                &point,
                &mut paths
                    .disk_space
                    .at(output.parent().unwrap().to_owned())
                    .writer(&mut file)?,
            )
            .and_then(|_| file.sync_all().map_err(Into::into));
        drop(file);
        if let Err(cause) = written {
            let _ = fs::remove_file(&output);
            return Err(cause);
        }
        Ok(output)
    })();
    let cleanup = fs::remove_dir_all(&working);
    let output = result?;
    cleanup?;
    Ok(output)
}
pub(super) fn prepare(
    paths: &RuntimePaths,
    marker: &Path,
    password: &str,
    file: fs::File,
    provider: &str,
) -> Result<package::PreparedRestore> {
    let (repo, id) = repository::Repository::from_portable(file, password)?;
    if repo.commit(&id)?.provider != provider {
        return Err(invalid("便携恢复点数据库类型不匹配。"));
    }
    repository::restore::prepare(
        paths,
        &repo,
        &id,
        marker,
        &json!({"fileMode":"Local","targetLocation":""}),
    )
}
