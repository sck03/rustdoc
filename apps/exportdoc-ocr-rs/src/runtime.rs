pub(crate) fn initialize() -> anyhow::Result<()> {
    use anyhow::{bail, Context};
    use std::{env, path::PathBuf};

    let library =
        PathBuf::from(env::var_os("ORT_DYLIB_PATH").context("ORT_DYLIB_PATH is required")?);
    if !library.is_absolute() {
        bail!("ORT_DYLIB_PATH must be an absolute application resource path");
    }
    #[cfg(windows)]
    let root = library
        .parent()
        .context("ONNX Runtime directory is missing")?;
    #[cfg(windows)]
    preload_crt(root)?;
    ort::init_from(&library)?.commit();
    Ok(())
}

#[cfg(windows)]
fn preload_crt(root: &std::path::Path) -> anyhow::Result<()> {
    use anyhow::{bail, Context};
    use serde::Deserialize;
    #[derive(Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct Manifest {
        runtime_architecture: String,
        files: Vec<Library>,
    }
    #[derive(Deserialize)]
    struct Library {
        name: String,
    }

    let path = root.join("msvc-runtime.json");
    if !path.try_exists()? {
        return Ok(());
    }
    let manifest: Manifest = serde_json::from_reader(std::fs::File::open(path)?)?;
    let architecture = if cfg!(target_arch = "aarch64") {
        "arm64"
    } else {
        "x64"
    };
    if manifest.runtime_architecture != architecture
        || manifest.files.is_empty()
        || manifest.files.len() > 8
    {
        bail!("packaged CRT manifest does not match the worker architecture");
    }
    // The governed list is ordered by dependencies. ARM64 needs three libraries;
    // the archive's vcruntime140_1.dll_arm64 is actually x64 and must not be loaded.
    for library in manifest.files {
        let name = library.name;
        if !name.ends_with(".dll")
            || !name
                .bytes()
                .all(|c| c.is_ascii_alphanumeric() || c == b'_' || c == b'.')
        {
            bail!("invalid packaged CRT library name");
        }
        ort::util::preload_dylib(root.join(&name))
            .with_context(|| format!("cannot load packaged {name}"))?;
    }
    Ok(())
}
