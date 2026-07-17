use std::{
    env,
    error::Error,
    fs,
    path::{Component, Path, PathBuf},
};

use crate::runtime_paths::RuntimePaths;

const RUNTIME_LAYOUT_MANIFEST_FILE_NAME: &str = "runtime-layout.json";

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeLayoutManifest {
    schema_version: u32,
    target: String,
    #[serde(default)]
    program_root_resources: Vec<RuntimeLayoutEntry>,
    #[serde(default)]
    runtime_data_directories: Vec<RuntimeLayoutEntry>,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeLayoutEntry {
    name: String,
    storage: String,
    kind: String,
    required: bool,
    relative_path: String,
}

pub(crate) fn validate_runtime_layout(paths: &RuntimePaths) -> Result<(), Box<dyn Error>> {
    let manifest_path = runtime_layout_manifest_path(&paths.app_root);
    if !manifest_path.exists() {
        if is_path_under_root(&paths.sidecar_path, &paths.app_root.join("sidecar")) {
            return Err(format!(
                "Tauri runtime layout manifest was not found: '{}'. Rebuild the Tauri bundle resources.",
                manifest_path.display()
            )
            .into());
        }

        return Ok(());
    }

    let manifest_text = fs::read_to_string(&manifest_path).map_err(|error| {
        format!(
            "Failed to read Tauri runtime layout manifest '{}': {error}",
            manifest_path.display()
        )
    })?;
    let manifest: RuntimeLayoutManifest =
        serde_json::from_str(&manifest_text).map_err(|error| {
            format!(
                "Failed to parse Tauri runtime layout manifest '{}': {error}",
                manifest_path.display()
            )
        })?;

    if manifest.schema_version != 1 {
        return Err(format!(
            "Unsupported Tauri runtime layout manifest schema version: {}",
            manifest.schema_version
        )
        .into());
    }

    if manifest.target != "tauri-desktop" {
        return Err(format!(
            "Unexpected Tauri runtime layout target '{}'.",
            manifest.target
        )
        .into());
    }

    for entry in &manifest.program_root_resources {
        validate_entry(
            entry,
            "program-root",
            &paths.app_root,
            "program root resource",
        )?;
    }

    for entry in &manifest.runtime_data_directories {
        validate_entry(
            entry,
            "runtime-data-root",
            &paths.data_root,
            "runtime data directory",
        )?;
    }

    Ok(())
}

fn validate_entry(
    entry: &RuntimeLayoutEntry,
    expected_storage: &str,
    root: &Path,
    description: &str,
) -> Result<(), Box<dyn Error>> {
    if entry.storage != expected_storage {
        return Err(format!(
            "Tauri runtime layout {} '{}' must use storage '{}', got '{}'.",
            description, entry.name, expected_storage, entry.storage
        )
        .into());
    }

    let path = resolve_layout_relative_path(root, &entry.relative_path, &entry.name)?;
    if !is_path_under_root(&path, root) {
        return Err(format!(
            "Tauri runtime layout {} '{}' must stay under '{}', got '{}'.",
            description,
            entry.name,
            root.display(),
            path.display()
        )
        .into());
    }

    if !entry.required {
        return Ok(());
    }

    match entry.kind.as_str() {
        "directory" => {
            if !path.is_dir() {
                return Err(format!(
                    "Required Tauri runtime layout directory '{}' was not found at '{}'.",
                    entry.name,
                    path.display()
                )
                .into());
            }
        }
        "file" => {
            if !path.is_file() {
                return Err(format!(
                    "Required Tauri runtime layout file '{}' was not found at '{}'.",
                    entry.name,
                    path.display()
                )
                .into());
            }
        }
        other => {
            return Err(format!(
                "Tauri runtime layout entry '{}' has unsupported kind '{}'.",
                entry.name, other
            )
            .into());
        }
    }

    Ok(())
}

fn resolve_layout_relative_path(
    root: &Path,
    relative_path: &str,
    name: &str,
) -> Result<PathBuf, Box<dyn Error>> {
    if relative_path.trim().is_empty() {
        return Err(format!(
            "Tauri runtime layout entry '{}' has an empty relative path.",
            name
        )
        .into());
    }

    let relative_path = Path::new(relative_path);
    if relative_path.is_absolute() {
        return Err(format!(
            "Tauri runtime layout entry '{}' must use a relative path.",
            name
        )
        .into());
    }

    for component in relative_path.components() {
        match component {
            Component::ParentDir | Component::Prefix(_) | Component::RootDir => {
                return Err(format!(
                    "Tauri runtime layout entry '{}' must not escape its storage root.",
                    name
                )
                .into());
            }
            Component::CurDir | Component::Normal(_) => {}
        }
    }

    Ok(root.join(relative_path))
}

fn runtime_layout_manifest_path(app_root: &Path) -> PathBuf {
    app_root.join(RUNTIME_LAYOUT_MANIFEST_FILE_NAME)
}

fn is_path_under_root(path: &Path, root: &Path) -> bool {
    let normalized_path = normalize_path_for_prefix(path);
    let normalized_root = normalize_path_for_prefix(root);

    if cfg!(windows) {
        let normalized_path = normalized_path
            .to_string_lossy()
            .replace('/', "\\")
            .to_ascii_lowercase();
        let normalized_root = normalized_root
            .to_string_lossy()
            .replace('/', "\\")
            .to_ascii_lowercase();

        return normalized_path == normalized_root
            || normalized_path
                .starts_with(&format!("{}\\", normalized_root.trim_end_matches('\\')));
    }

    normalized_path == normalized_root || normalized_path.starts_with(normalized_root)
}

fn normalize_path_for_prefix(path: &Path) -> PathBuf {
    if path.is_absolute() {
        return path.to_path_buf();
    }

    env::current_dir()
        .map(PathBuf::from)
        .unwrap_or_else(|_| PathBuf::from("."))
        .join(path)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_packaged_runtime_layout_manifest() {
        let root = fresh_test_dir("valid-runtime-layout");
        let app_root = root.join("app");
        let data_root = root.join("data");
        let sidecar_path = app_root.join("sidecar").join(sidecar_file_name());
        create_file(&sidecar_path);
        for directory in ["Templates", "OcrModels", "Resources", "Browsers"] {
            fs::create_dir_all(app_root.join(directory)).unwrap();
        }
        for directory in [
            "Database",
            "SingleWindow",
            "Backups",
            "Cache",
            "Config",
            "WebView",
            "Logs",
        ] {
            fs::create_dir_all(data_root.join(directory)).unwrap();
        }
        write_manifest(
            &app_root,
            &format!(
                r#"{{
  "schemaVersion": 1,
  "target": "tauri-desktop",
  "programRootResources": [
    {{ "name": "api-sidecar", "storage": "program-root", "kind": "file", "required": true, "relativePath": "sidecar/{sidecar}" }},
    {{ "name": "Templates", "storage": "program-root", "kind": "directory", "required": true, "relativePath": "Templates" }},
    {{ "name": "OcrModels", "storage": "program-root", "kind": "directory", "required": true, "relativePath": "OcrModels" }},
    {{ "name": "Resources", "storage": "program-root", "kind": "directory", "required": true, "relativePath": "Resources" }},
    {{ "name": "Browsers", "storage": "program-root", "kind": "directory", "required": true, "relativePath": "Browsers" }}
  ],
  "runtimeDataDirectories": [
    {{ "name": "Database", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Database" }},
    {{ "name": "SingleWindow", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "SingleWindow" }},
    {{ "name": "Backups", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Backups" }},
    {{ "name": "Cache", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Cache" }},
    {{ "name": "Config", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Config" }},
    {{ "name": "WebView", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "WebView" }},
    {{ "name": "Logs", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Logs" }}
  ]
}}"#,
                sidecar = sidecar_file_name()
            ),
        );

        validate_runtime_layout(&paths(app_root, data_root, sidecar_path)).unwrap();
    }

    #[test]
    fn requires_manifest_when_packaged_sidecar_is_used() {
        let root = fresh_test_dir("missing-runtime-layout");
        let app_root = root.join("app");
        let data_root = root.join("data");
        let sidecar_path = app_root.join("sidecar").join(sidecar_file_name());
        create_file(&sidecar_path);

        let error = validate_runtime_layout(&paths(app_root, data_root, sidecar_path))
            .unwrap_err()
            .to_string();

        assert!(error.contains("runtime layout manifest"));
    }

    #[test]
    fn allows_missing_manifest_for_external_development_sidecar() {
        let root = fresh_test_dir("external-sidecar-runtime-layout");
        let app_root = root.join("app");
        let data_root = root.join("data");
        let sidecar_path = root.join("external").join(sidecar_file_name());
        create_file(&sidecar_path);

        validate_runtime_layout(&paths(app_root, data_root, sidecar_path)).unwrap();
    }

    #[test]
    fn rejects_layout_entries_that_escape_storage_root() {
        let root = fresh_test_dir("escaping-runtime-layout");
        let app_root = root.join("app");
        let data_root = root.join("data");
        let sidecar_path = app_root.join("sidecar").join(sidecar_file_name());
        create_file(&sidecar_path);
        write_manifest(
            &app_root,
            r#"{
  "schemaVersion": 1,
  "target": "tauri-desktop",
  "programRootResources": [
    { "name": "bad", "storage": "program-root", "kind": "file", "required": true, "relativePath": "../outside.txt" }
  ],
  "runtimeDataDirectories": []
}"#,
        );

        let error = validate_runtime_layout(&paths(app_root, data_root, sidecar_path))
            .unwrap_err()
            .to_string();

        assert!(error.contains("must not escape"));
    }

    #[test]
    fn rejects_missing_required_runtime_data_directory() {
        let root = fresh_test_dir("missing-runtime-data-directory");
        let app_root = root.join("app");
        let data_root = root.join("data");
        let sidecar_path = app_root.join("sidecar").join(sidecar_file_name());
        create_file(&sidecar_path);
        write_manifest(
            &app_root,
            r#"{
  "schemaVersion": 1,
  "target": "tauri-desktop",
  "programRootResources": [],
  "runtimeDataDirectories": [
    { "name": "Config", "storage": "runtime-data-root", "kind": "directory", "required": true, "relativePath": "Config" }
  ]
}"#,
        );

        let error = validate_runtime_layout(&paths(app_root, data_root, sidecar_path))
            .unwrap_err()
            .to_string();

        assert!(error.contains("Required Tauri runtime layout directory"));
    }

    fn paths(app_root: PathBuf, data_root: PathBuf, sidecar_path: PathBuf) -> RuntimePaths {
        RuntimePaths {
            log_root: data_root.join("Logs"),
            app_root,
            data_root,
            sidecar_path,
        }
    }

    fn write_manifest(app_root: &Path, text: &str) {
        fs::create_dir_all(app_root).unwrap();
        fs::write(runtime_layout_manifest_path(app_root), text).unwrap();
    }

    fn create_file(path: &Path) {
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, b"test").unwrap();
    }

    fn sidecar_file_name() -> &'static str {
        if cfg!(windows) {
            "ExportDocManager.Api.exe"
        } else {
            "ExportDocManager.Api"
        }
    }

    fn fresh_test_dir(name: &str) -> PathBuf {
        let root = env::current_dir()
            .unwrap()
            .join("target")
            .join("runtime-layout-tests")
            .join(format!("{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        root
    }
}
