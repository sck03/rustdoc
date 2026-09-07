use std::{env, fs, path::PathBuf};

fn main() {
    println!("cargo:rerun-if-env-changed=EXPORTDOCMANAGER_PRODUCT_EDITION");
    configure_product_edition();
    ensure_debug_resource_root();
    let mut windows = tauri_build::WindowsAttributes::new();

    if let Some(icon_path) = copy_icon_to_out_dir() {
        windows = windows.window_icon_path(icon_path);
    }

    let attributes = tauri_build::Attributes::new().windows_attributes(windows);
    tauri_build::try_build(attributes).expect("failed to run Tauri build script");
}

fn configure_product_edition() {
    let manifest_dir =
        PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("missing Cargo manifest directory"));
    let path = manifest_dir.join("../../../scripts/product-editions.json");
    println!("cargo:rerun-if-changed={}", path.display());
    let catalog: serde_json::Value =
        serde_json::from_str(&fs::read_to_string(path).expect("missing product edition catalog"))
            .expect("invalid product edition catalog");
    let editions = catalog["editions"]
        .as_object()
        .expect("missing product editions");
    let requested = env::var("EXPORTDOCMANAGER_PRODUCT_EDITION").unwrap_or_else(|_| "Full".into());
    let (edition, metadata) = editions
        .iter()
        .find(|(name, _)| name.eq_ignore_ascii_case(requested.trim()))
        .unwrap_or_else(|| panic!("unsupported product edition: {requested}"));
    let product_name = metadata["productName"]
        .as_str()
        .expect("missing product name");
    println!("cargo:rustc-env=EXPORTDOCMANAGER_PRODUCT_EDITION={edition}");
    println!("cargo:rustc-env=EXPORTDOCMANAGER_PRODUCT_NAME={product_name}");
}

fn ensure_debug_resource_root() {
    if env::var("PROFILE").as_deref() != Ok("debug") {
        return;
    }

    let Some(manifest_dir) = env::var("CARGO_MANIFEST_DIR").ok().map(PathBuf::from) else {
        return;
    };
    let resource_root = manifest_dir.join("../../../artifacts/tauri-bundle/resources");
    fs::create_dir_all(resource_root)
        .expect("failed to create the empty debug Tauri resource root");
}

fn copy_icon_to_out_dir() -> Option<PathBuf> {
    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").ok()?);
    let source = manifest_dir.join("icons/app.ico");

    println!("cargo:rerun-if-changed={}", source.display());

    if !source.exists() {
        return None;
    }

    let out_dir = PathBuf::from(env::var("OUT_DIR").ok()?);
    let target = out_dir.join("export-doc-manager.ico");
    fs::copy(&source, &target).expect("failed to copy Tauri window icon into OUT_DIR");

    Some(target)
}
