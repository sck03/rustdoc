use std::path::PathBuf;

pub(crate) fn workspace_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .canonicalize()
        .unwrap()
}
/// Declare this guard after every database/process owner in a fixture.
pub(crate) struct ScratchDirectory(pub PathBuf);
impl ScratchDirectory {
    pub(crate) fn new() -> Self {
        let directory = Self(
            workspace_root()
                .join(".codex-runtime/unit-tests")
                .join(crate::paths::nonce().unwrap()),
        );
        crate::paths::ensure_safe_absolute(&directory.0).unwrap();
        std::fs::create_dir_all(directory.0.join("Cache")).unwrap();
        std::fs::create_dir_all(directory.0.join("Logs")).unwrap();
        directory
    }
    pub(crate) fn paths(&self) -> crate::paths::RuntimePaths {
        crate::paths::RuntimePaths {
            app_root: self.0.clone(),
            data_root: self.0.clone(),
            cache_root: self.0.join("Cache"),
            log_root: self.0.join("Logs"),
            font_path: self.0.join("font.otf"),
        }
    }
    pub(crate) fn service(&self) -> TestService {
        TestService::open(
            crate::paths::RuntimePaths::server(&self.0, &self.0.join("Data")).unwrap(),
        )
    }
}
pub(crate) struct TestService(std::sync::Arc<super::NativeService>);
impl TestService {
    pub(crate) fn open(paths: crate::paths::RuntimePaths) -> Self {
        Self(super::NativeService::open(paths).unwrap())
    }
}
impl std::ops::Deref for TestService {
    type Target = std::sync::Arc<super::NativeService>;
    fn deref(&self) -> &Self::Target {
        &self.0
    }
}
impl Drop for TestService {
    fn drop(&mut self) {
        cleanup(self.0.close(), "test service");
    }
}
fn cleanup<E: std::fmt::Display>(result: std::result::Result<(), E>, label: &str) {
    if let Err(cause) = result {
        if std::thread::panicking() {
            eprintln!("{label} cleanup failed: {cause}");
        } else {
            panic!("{label} cleanup failed: {cause}");
        }
    }
}
pub(crate) fn actor(name: &str) -> super::store::Actor {
    super::store::Actor {
        scope_rules: vec![],
        context: String::new(),
        edition: Default::default(),
        id: 1,
        name: name.into(),
        company: "DEFAULT".into(),
        department: "GENERAL".into(),
        admin: false,
        grants: vec![],
        departments: vec![],
        oa_assigned_only: false,
    }
}
pub(crate) fn administrator() -> super::store::Actor {
    super::store::Actor {
        admin: true,
        ..actor("系统管理员")
    }
}
impl Drop for ScratchDirectory {
    fn drop(&mut self) {
        if !self.0.exists() {
            return;
        }
        let workspace = workspace_root().join(".codex-runtime");
        assert!(
            self.0.starts_with(&workspace) && self.0 != workspace && !self.0.join(".git").exists(),
            "test cleanup escaped its runtime root"
        );
        crate::paths::ensure_safe_absolute(&self.0).expect("unsafe test cleanup path");
        cleanup(
            std::fs::remove_dir_all(&self.0),
            &format!("test directory {}", self.0.display()),
        );
    }
}
