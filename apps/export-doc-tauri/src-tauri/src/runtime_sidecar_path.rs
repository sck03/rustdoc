use std::{
    env,
    error::Error,
    path::{Path, PathBuf},
};

pub(crate) fn resolve_sidecar_path(app_root: &Path) -> Result<PathBuf, Box<dyn Error>> {
    if let Some(path) = env::var_os("EXPORTDOCMANAGER_API_SIDECAR").map(PathBuf::from) {
        if path.exists() {
            return Ok(path);
        }
    }

    let file_name = sidecar_file_name();
    let mut candidates = vec![app_root.join("sidecar").join(file_name)];

    if let Some(repo_root) = repo_root_from_manifest() {
        candidates.push(
            repo_root
                .join("src")
                .join("ExportDocManager.Api")
                .join("bin")
                .join("Debug")
                .join("net10.0")
                .join(file_name),
        );
        candidates.push(
            repo_root
                .join("src")
                .join("ExportDocManager.Api")
                .join("bin")
                .join("Release")
                .join("net10.0")
                .join("publish")
                .join(file_name),
        );
    }

    candidates
        .into_iter()
        .find(|path| path.exists())
        .ok_or_else(|| {
            format!(
                "API sidecar executable was not found. Set EXPORTDOCMANAGER_API_SIDECAR or publish it to '{}'.",
                app_root.join("sidecar").display()
            )
            .into()
        })
}

pub(crate) fn sidecar_file_name() -> &'static str {
    if cfg!(windows) {
        "ExportDocManager.Api.exe"
    } else {
        "ExportDocManager.Api"
    }
}

fn repo_root_from_manifest() -> Option<PathBuf> {
    let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    manifest_dir
        .parent()?
        .parent()?
        .parent()
        .map(Path::to_path_buf)
}
