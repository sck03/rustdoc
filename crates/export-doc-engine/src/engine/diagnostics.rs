use super::{error::Result, store};
use crate::{contracts, paths::RuntimePaths};
use serde_json::{Value, json};

pub fn health(paths: &RuntimePaths, provider: &str) -> Result<Value> {
    let mut value = contracts::initial(contracts::schema("ApiHealthResponse"));
    let roots = [
        ("appRoot", "程序", paths.app_root.clone(), "read"),
        (
            "dataRoot",
            "业务数据",
            paths.data_root.clone(),
            "read-write",
        ),
        ("logRoot", "运行日志", paths.log_root.clone(), "read-write"),
        ("cacheRoot", "缓存", paths.cache_root.clone(), "read-write"),
    ];
    let mut runtime_paths = vec![];
    for (key, label, path, access) in roots {
        value[key] = json!(path);
        runtime_paths.push(json!({"key":key,"label":label,"path":path,"storageClass":if access=="read"{"application"}else{"data"},"accessMode":access,"requirement":"required","exists":path.is_dir(),"description":"位于显式配置的运行根目录中"}));
    }
    value["status"] = json!("Healthy");
    value["checkedAt"] = json!(store::timestamp());
    value["productVersion"] = json!(env!("CARGO_PKG_VERSION"));
    value["informationalVersion"] = json!(concat!(env!("CARGO_PKG_VERSION"), " Rust"));
    value["databaseProvider"] = json!(provider);
    value["databaseProviderKey"] = json!(if provider == "SQLite" {
        "sqlite"
    } else {
        "postgresql"
    });
    value["databaseRoot"] = json!(paths.data_root);
    value["sqliteDatabasePath"] = json!(if provider == "SQLite" {
        paths
            .data_root
            .join("exportdoc-native.db")
            .to_string_lossy()
            .into_owned()
    } else {
        String::new()
    });
    value["templateRoot"] = json!(paths.data_root.join("Templates"));
    value["singleWindowRoot"] = json!(paths.data_root.join("SingleWindow"));
    value["ocrModelRoot"] = json!(paths.app_root.join("OcrModels"));
    value["runtimePaths"] = json!(runtime_paths);
    value["runtimeDependencies"] = json!([
        {"key":"database","label":provider,"requirement":"required","status":"ready","ready":true,"resolvedPath":value["sqliteDatabasePath"],"message":"当前数据库连接及实例锁正常"},
        {"key":"report-font","label":"报表字体","requirement":"optional","status":if paths.font_path.is_file(){"ready"}else{"missing"},"ready":paths.font_path.is_file(),"resolvedPath":paths.font_path,"message":"使用随包受控字体进行原生排版"}
    ]);
    #[cfg(feature = "ocr")]
    if let Some(items) = value["runtimeDependencies"].as_array_mut() {
        items.push(super::ocr::diagnostic(paths));
    }
    value["storagePolicy"] =
        json!("数据库、配置、缓存和输出均使用受管运行目录；桌面运行不需要 WebView 或 .NET。");
    Ok(value)
}
