use export_doc_engine::{
    api::ApiClient,
    contracts,
    generated_api::*,
    paths::{RuntimePaths, nonce},
};
use serde_json::{Value, json};
use std::{fs, path::PathBuf};

pub struct Fixture {
    pub root: PathBuf,
    pub paths: RuntimePaths,
    pub client: Option<ApiClient>,
}
impl Fixture {
    pub fn new() -> Self {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime")
            .join("native-tests")
            .join(nonce().unwrap());
        fs::create_dir_all(root.join("Cache")).unwrap();
        fs::create_dir_all(root.join("Logs")).unwrap();
        let paths = RuntimePaths {
            app_root: root.clone(),
            data_root: root.clone(),
            cache_root: root.join("Cache"),
            log_root: root.join("Logs"),
            font_path: root.join("font.otf"),
        };
        let client = ApiClient::native(paths.clone())
            .unwrap()
            .login("admin".into(), String::new())
            .unwrap()
            .0;
        Self {
            root,
            paths,
            client: Some(client),
        }
    }
    pub fn client(&self) -> &ApiClient {
        self.client.as_ref().unwrap()
    }
    pub fn request(&self, op: Operation, id: Option<i64>, body: Option<Value>) -> Value {
        self.client()
            .json(
                op,
                &id.map(|id| vec![("id", id.to_string())])
                    .unwrap_or_default(),
                &[],
                body,
            )
            .unwrap_or_else(|error| panic!("{}: {error}", op.id))
    }
    pub fn create(&self, op: Operation, body: Value) -> Value {
        self.request(
            op,
            None,
            Some(contracts::overlay(contracts::object(op.id, true), &body)),
        )
    }
    pub fn employee(&self) -> Value {
        self.create(CREATE_PERSONNEL,json!({"requestKey":nonce().unwrap(),"employeeNumber":format!("EMP-{}",&nonce().unwrap()[..8]),"departmentId":"GENERAL","jobTitle":"业务助理","employmentType":"FullTime","hireDate":"2026-09-01","profile":{"fullName":"测试人员"},"onProbation":true}))
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        self.client.take();
        let parent = self.root.parent().unwrap();
        if parent
            .file_name()
            .is_some_and(|name| name == "native-tests")
            && self
                .root
                .file_name()
                .is_some_and(|name| name.to_string_lossy().len() == 32)
        {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}
