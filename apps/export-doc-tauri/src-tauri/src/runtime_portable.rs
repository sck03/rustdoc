use std::{
    env,
    error::Error,
    fs,
    path::{Path, PathBuf},
};

const PORTABLE_ROOT_ENVIRONMENT_VARIABLE: &str = "EXPORTDOCMANAGER_PORTABLE_ROOT";
pub(crate) const PORTABLE_RUNTIME_MARKER_FILE_NAME: &str = "portable-runtime.json";
pub(crate) const RUNTIME_LAYOUT_MANIFEST_FILE_NAME: &str = "runtime-layout.json";

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct PortableRuntimeMarker {
    schema_version: u32,
    mode: String,
}

pub(crate) fn resolve_portable_runtime_root(
    app_root: &Path,
    argument_root: Option<PathBuf>,
) -> Result<Option<PathBuf>, Box<dyn Error>> {
    if let Some(explicit_root) =
        argument_root.or_else(|| env::var_os(PORTABLE_ROOT_ENVIRONMENT_VARIABLE).map(PathBuf::from))
    {
        validate_portable_runtime_marker(&explicit_root, app_root)?;
        return Ok(Some(explicit_root));
    }

    if is_portable_runtime(app_root)? {
        return Ok(Some(app_root.to_path_buf()));
    }

    if let Some(external_root) = platform_portable_runtime_root()? {
        if external_root
            .join(PORTABLE_RUNTIME_MARKER_FILE_NAME)
            .is_file()
        {
            validate_portable_runtime_marker(&external_root, app_root)?;
            return Ok(Some(external_root));
        }
    }

    Ok(None)
}

pub(crate) fn is_portable_runtime(app_root: &Path) -> Result<bool, Box<dyn Error>> {
    let marker_path = app_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME);
    if !marker_path.exists() {
        return Ok(false);
    }

    validate_portable_runtime_marker(app_root, app_root)?;
    Ok(true)
}

pub(crate) fn validate_portable_runtime_marker(
    marker_root: &Path,
    app_root: &Path,
) -> Result<(), Box<dyn Error>> {
    let marker_path = marker_root.join(PORTABLE_RUNTIME_MARKER_FILE_NAME);
    if !marker_path.is_file() {
        return Err(format!(
            "Portable runtime root '{}' is missing '{}'.",
            marker_root.display(),
            PORTABLE_RUNTIME_MARKER_FILE_NAME
        )
        .into());
    }

    if !app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME).is_file() {
        return Err(format!(
            "Portable runtime marker '{}' exists, but the packaged resource root '{}' is missing '{}'.",
            marker_path.display(),
            app_root.display(),
            RUNTIME_LAYOUT_MANIFEST_FILE_NAME
        )
        .into());
    }

    let marker_text = fs::read_to_string(&marker_path).map_err(|error| {
        format!(
            "Failed to read portable runtime marker '{}': {error}",
            marker_path.display()
        )
    })?;
    let marker: PortableRuntimeMarker = serde_json::from_str(&marker_text).map_err(|error| {
        format!(
            "Failed to parse portable runtime marker '{}': {error}",
            marker_path.display()
        )
    })?;
    if marker.schema_version != 1 || marker.mode != "portable" {
        return Err(format!(
            "Unsupported portable runtime marker '{}': expected schemaVersion=1 and mode='portable'.",
            marker_path.display()
        )
        .into());
    }

    Ok(())
}

#[cfg(target_os = "linux")]
fn platform_portable_runtime_root() -> Result<Option<PathBuf>, Box<dyn Error>> {
    let Some(app_image) = env::var_os("APPIMAGE") else {
        return Ok(None);
    };
    let app_image = PathBuf::from(app_image);
    let app_image = if app_image.is_absolute() {
        app_image
    } else {
        env::current_dir()?.join(app_image)
    };
    Ok(app_image.parent().map(Path::to_path_buf))
}

#[cfg(target_os = "macos")]
fn platform_portable_runtime_root() -> Result<Option<PathBuf>, Box<dyn Error>> {
    let executable = env::current_exe()?;
    Ok(macos_bundle_parent(&executable))
}

#[cfg(target_os = "macos")]
fn macos_bundle_parent(executable: &Path) -> Option<PathBuf> {
    let app_bundle = executable.parent()?.parent()?.parent()?;
    if !app_bundle
        .extension()
        .is_some_and(|extension| extension.eq_ignore_ascii_case("app"))
    {
        return None;
    }
    app_bundle.parent().map(Path::to_path_buf)
}

#[cfg(not(any(target_os = "linux", target_os = "macos")))]
fn platform_portable_runtime_root() -> Result<Option<PathBuf>, Box<dyn Error>> {
    Ok(None)
}
