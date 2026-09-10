#[cfg(all(windows, target_arch = "x86_64"))]
pub(crate) fn initialize() -> anyhow::Result<()> {
    use anyhow::{bail, Context};
    use std::{env, path::PathBuf};

    let library =
        PathBuf::from(env::var_os("ORT_DYLIB_PATH").context("ORT_DYLIB_PATH is required")?);
    if !library.is_absolute() {
        bail!("ORT_DYLIB_PATH must be an absolute application resource path");
    }
    let root = library
        .parent()
        .context("ONNX Runtime directory is missing")?;
    if root.join("msvc-runtime.json").try_exists()? {
        // ONNX is loaded from its explicit resource path. Pin its CRT dependencies to
        // the same package before ort loads it; no system installation is needed.
        for name in [
            "vcruntime140.dll",
            "vcruntime140_1.dll",
            "msvcp140.dll",
            "msvcp140_1.dll",
        ] {
            ort::util::preload_dylib(root.join(name))
                .with_context(|| format!("cannot load packaged {name}"))?;
        }
    }
    ort::init_from(&library)?.commit();
    Ok(())
}

#[cfg(not(all(windows, target_arch = "x86_64")))]
pub(crate) fn initialize() -> anyhow::Result<()> {
    Ok(())
}
