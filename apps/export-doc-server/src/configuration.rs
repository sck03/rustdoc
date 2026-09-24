use export_doc_engine::paths::{RuntimePaths, ensure_safe_absolute};
use std::{env, fs, net::SocketAddr, path::PathBuf};

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
    pub business_clock: export_doc_engine::clock::BusinessClock,
}

fn secret(key: &str) -> Result<String, String> {
    let direct = env::var(key).unwrap_or_default();
    let path = env::var(format!("{key}_FILE")).unwrap_or_default();
    if !direct.is_empty() && !path.is_empty() {
        return Err(format!("{key} 与其文件设置不能同时提供。"));
    }
    if path.is_empty() {
        return Ok(direct);
    }
    let path = PathBuf::from(path);
    ensure_safe_absolute(&path)?;
    let metadata = fs::metadata(&path).map_err(|_| format!("{key} 的配置文件不可读。"))?;
    if !metadata.is_file() || metadata.len() > 16384 {
        return Err(format!("{key} 的配置文件无效。"));
    }
    fs::read_to_string(&path)
        .map(|value| value.trim_end_matches(['\r', '\n']).to_owned())
        .map_err(|_| format!("{key} 的配置文件编码无效。"))
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
                "--initialize-only" => {
                    initialize_schema = true;
                    initialize_only = true;
                }
                _ => return Err(format!("未知服务参数：{argument}")),
            }
        }
        let data_root = data_root.unwrap_or_else(|| app_root.join("App_Data"));
        let paths = RuntimePaths::server(&app_root, &data_root)?;
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
        let connection = secret("EXPORTDOCMANAGER_POSTGRES_CONNECTION")?;
        if connection.is_empty() && !initialize_only {
            return Err("团队版必须设置 PostgreSQL 18 业务连接。".into());
        }
        if initialize_schema && restore_pending {
            return Err("建表与恢复不能同时执行。".into());
        }
        let maintenance_connection = if initialize_schema || restore_pending {
            secret("EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION")?
        } else {
            String::new()
        };
        if (initialize_schema || restore_pending) && maintenance_connection.is_empty() {
            return Err("建表需要独立的 PostgreSQL 维护连接。".into());
        }
        Ok(Self {
            paths,
            bind,
            web_root,
            endpoint_file,
            connection,
            maintenance_connection,
            owner: env::var("EXPORTDOCMANAGER_POSTGRES_OWNER")
                .unwrap_or_else(|_| "exportdoc_owner".into()),
            bootstrap_token: secret("EXPORTDOCMANAGER_BOOTSTRAP_TOKEN")?,
            initialize_schema,
            initialize_only,
            restore_pending,
            business_clock: export_doc_engine::clock::BusinessClock::new(
                &env::var("EXPORTDOCMANAGER_BUSINESS_TIME_ZONE")
                    .unwrap_or_else(|_| "Asia/Shanghai".into()),
            )?,
        })
    }
}
