use export_doc_engine::paths::{RuntimePaths, ensure_safe_absolute};
use std::{
    env, fs,
    io::Read,
    net::SocketAddr,
    path::{Path, PathBuf},
};

pub struct Configuration {
    pub paths: RuntimePaths,
    pub bind: SocketAddr,
    pub web_root: Option<PathBuf>,
    pub endpoint_file: Option<PathBuf>,
    pub connection: String,
    pub maintenance_connection: String,
    pub owner: String,
    pub bootstrap_token: String,
    pub initialize_schema: bool,
    pub initialize_only: bool,
    pub restore_pending: bool,
    pub migration_package: Option<PathBuf>,
    pub migration_password: String,
    pub business_clock: export_doc_engine::clock::BusinessClock,
    pub pool: export_doc_storage::pool::PoolOptions,
}

fn secret(key: &str, fallback: Option<&Path>) -> Result<String, String> {
    let direct = env::var(key).unwrap_or_default();
    let path = env::var(format!("{key}_FILE")).unwrap_or_default();
    if !direct.is_empty() && !path.is_empty() {
        return Err(format!("{key} 与其文件设置不能同时提供。"));
    }
    if path.is_empty() && (!direct.is_empty() || fallback.is_none()) {
        return Ok(direct);
    }
    let path = if path.is_empty() {
        fallback.unwrap().to_path_buf()
    } else {
        PathBuf::from(path)
    };
    file_secret(key, &path)
}

fn file_secret(key: &str, path: &Path) -> Result<String, String> {
    ensure_safe_absolute(path)?;
    let metadata = fs::metadata(path).map_err(|_| format!("{key} 的配置文件不可读。"))?;
    if !metadata.is_file() || metadata.len() > 16384 {
        return Err(format!("{key} 的配置文件无效。"));
    }
    let mut value = String::new();
    fs::File::open(path)
        .and_then(|file| file.take(16385).read_to_string(&mut value))
        .map_err(|_| format!("{key} 的配置文件编码无效。"))?;
    if value.len() > 16384 {
        return Err(format!("{key} 的配置文件过大。"));
    }
    Ok(value.trim_end_matches(['\r', '\n']).to_owned())
}

impl Configuration {
    pub fn load() -> Result<Self, String> {
        let executable = env::current_exe().map_err(|error| error.to_string())?;
        let mut app_root = executable.parent().ok_or("程序目录无效。")?.to_path_buf();
        let mut data_root = None;
        let mut web_root = None;
        let mut endpoint_file = None;
        let mut bind: SocketAddr = "127.0.0.1:5188".parse().unwrap();
        let mut initialize_schema = false;
        let mut initialize_only = false;
        let mut restore_pending = false;
        let mut migration_package = None;
        let mut migration_password_file = None;
        let mut args = env::args().skip(1);
        while let Some(argument) = args.next() {
            match argument.as_str() {
                "--app-root" => app_root = PathBuf::from(args.next().ok_or("缺少 AppRoot。")?),
                "--data-root" => {
                    data_root = Some(PathBuf::from(args.next().ok_or("缺少 DataRoot。")?))
                }
                "--web-root" => {
                    web_root = Some(PathBuf::from(args.next().ok_or("缺少网页目录。")?))
                }
                "--endpoint-file" => {
                    endpoint_file = Some(PathBuf::from(args.next().ok_or("缺少端点文件。")?))
                }
                "--bind" => {
                    bind = args
                        .next()
                        .ok_or("缺少监听地址。")?
                        .parse()
                        .map_err(|_| "监听地址须为 IP:端口。")?
                }
                "--initialize-schema" => initialize_schema = true,
                "--restore-pending" => restore_pending = true,
                "--import-migration" => {
                    migration_package =
                        Some(PathBuf::from(args.next().ok_or("缺少迁移包文件路径。")?));
                    restore_pending = true;
                }
                "--package-password-file" => {
                    migration_password_file =
                        Some(PathBuf::from(args.next().ok_or("缺少包密码文件路径。")?))
                }
                "--initialize-only" => {
                    initialize_schema = true;
                    initialize_only = true;
                }
                _ => return Err(format!("未知服务参数：{argument}")),
            }
        }
        let data_root = data_root.unwrap_or_else(|| app_root.join("App_Data"));
        if migration_package.is_some() != migration_password_file.is_some() {
            return Err("--import-migration 与 --package-password-file 必须同时指定。".into());
        }
        if let Some(path) = &migration_package {
            ensure_safe_absolute(path)?;
        }
        let migration_password = migration_password_file
            .as_ref()
            .map(|path| file_secret("迁移包密码", path))
            .transpose()?
            .unwrap_or_default();
        let paths = RuntimePaths::server(&app_root, &data_root)?;
        let config_root = app_root.join("RuntimeConfig");
        let config_marker = config_root.join("deployment.json");
        ensure_safe_absolute(&config_marker)?;
        let prepared = config_marker
            .try_exists()
            .map_err(|_| "无法检查网页部署配置。")?;
        if prepared {
            let marker = file_secret("网页部署标记", &config_marker)?;
            let marker: serde_json::Value =
                serde_json::from_str(&marker).map_err(|_| "网页部署标记无效。")?;
            if marker["schemaVersion"] != 1 || marker["purpose"] != "exportdoc-web-deployment" {
                return Err("网页部署标记无效。".into());
            }
        }
        let fallback = |name: &str| prepared.then(|| config_root.join(name));
        if web_root.is_none()
            && app_root
                .join("Web")
                .try_exists()
                .map_err(|_| "无法检查网页目录。")?
        {
            web_root = Some(app_root.join("Web"));
        }
        if let Some(root) = &web_root {
            ensure_safe_absolute(root)?;
            if !root.join("index.html").is_file() {
                return Err("网页目录缺少构建后的 index.html。".into());
            }
        }
        if let Some(path) = &endpoint_file {
            ensure_safe_absolute(path)?;
            if !path.starts_with(&data_root) {
                return Err("端点文件必须位于本次 DataRoot 内。".into());
            }
        }
        let connection = if initialize_only {
            String::new()
        } else {
            secret(
                "EXPORTDOCMANAGER_POSTGRES_CONNECTION",
                fallback("app-connection.txt").as_deref(),
            )?
        };
        if connection.is_empty() && !initialize_only {
            return Err("团队版必须设置 PostgreSQL 18 业务连接。".into());
        }
        if initialize_schema && restore_pending {
            return Err("建表与恢复不能同时执行。".into());
        }
        let maintenance_connection = if initialize_schema || restore_pending {
            secret(
                "EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION",
                fallback("maintenance-connection.txt").as_deref(),
            )?
        } else {
            String::new()
        };
        if (initialize_schema || restore_pending) && maintenance_connection.is_empty() {
            return Err("建表需要独立的 PostgreSQL 维护连接。".into());
        }
        Ok(Self {
            pool: export_doc_storage::pool::PoolOptions {
                size: env::var("EXPORTDOCMANAGER_POSTGRES_POOL_SIZE")
                    .unwrap_or_else(|_| "4".into())
                    .parse()
                    .map_err(|_| "连接池大小必须为整数。")?,
                acquire_timeout: std::time::Duration::from_millis(
                    env::var("EXPORTDOCMANAGER_POSTGRES_POOL_WAIT_MS")
                        .unwrap_or_else(|_| "5000".into())
                        .parse()
                        .map_err(|_| "连接池等待时间必须为整数毫秒。")?,
                ),
            }
            .validate()
            .map_err(|cause| cause.to_string())?,
            paths,
            bind,
            web_root,
            endpoint_file,
            connection,
            maintenance_connection,
            owner: env::var("EXPORTDOCMANAGER_POSTGRES_OWNER")
                .unwrap_or_else(|_| "exportdoc_owner".into()),
            bootstrap_token: if initialize_only || restore_pending {
                String::new()
            } else {
                secret(
                    "EXPORTDOCMANAGER_BOOTSTRAP_TOKEN",
                    fallback("bootstrap-token.txt").as_deref(),
                )?
            },
            initialize_schema,
            initialize_only,
            restore_pending,
            migration_package,
            migration_password,
            business_clock: export_doc_engine::clock::BusinessClock::new(
                &env::var("EXPORTDOCMANAGER_BUSINESS_TIME_ZONE")
                    .unwrap_or_else(|_| "Asia/Shanghai".into()),
            )?,
        })
    }
}
