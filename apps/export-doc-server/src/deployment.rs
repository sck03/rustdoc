//! Offline deployment preparation. It never starts Docker or receives database administrator credentials.
use export_doc_engine::{paths, secrets::private_directory};
use std::{
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
};

const IMAGE_COMPOSE: &str = include_str!("../../../deploy/rust-native/compose.image.yml");
const POSTGRES_INIT: &str = include_str!("../../../deploy/rust-native/postgres-init.sh");
const POSTGRES_ROLES: &str = include_str!("../../../deploy/rust-native/postgres-roles.sql");
const DEFAULT_IMAGE: &str = "ghcr.io/sck03/exportdoc-rust-native:latest";
type Result<T> = std::result::Result<T, Box<dyn std::error::Error>>;

pub fn command(args: &[String]) -> Option<Result<()>> {
    match args.first().map(String::as_str) {
        Some("--print-compose") => Some((|| {
            if args.len() > 2 {
                return Err("用法：--print-compose [镜像名称:版本]".into());
            }
            let image = args.get(1).map_or(DEFAULT_IMAGE, String::as_str);
            if image.is_empty()
                || !image
                    .bytes()
                    .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || b"./:_@-".contains(&c))
            {
                return Err("镜像名称无效。".into());
            }
            std::io::stdout()
                .lock()
                .write_all(IMAGE_COMPOSE.replace(DEFAULT_IMAGE, image).as_bytes())?;
            Ok(())
        })()),
        Some("--prepare-docker") => Some((|| {
            if !(2..=3).contains(&args.len()) {
                return Err("用法：--prepare-docker <部署卷根目录>".into());
            }
            prepare(&PathBuf::from(&args[1]), true)?;
            if let Some(data) = args.get(2) {
                prepare_data(Path::new(data))?;
            }
            eprintln!("部署凭据已准备并核对。");
            Ok(())
        })()),
        Some("--setup-web") => Some((|| {
            if args.len() > 2 {
                return Err("用法：--setup-web [程序目录]".into());
            }
            let root = match args.get(1) {
                Some(root) => PathBuf::from(root),
                None => std::env::current_exe()?
                    .parent()
                    .ok_or("程序目录无效。")?
                    .to_path_buf(),
            }
            .join("RuntimeConfig");
            prepare(&root, false)?;
            eprintln!(
                "配置已保存到 {}。首次用 PostgreSQL 管理员执行其中 initialize.sql，然后运行 --initialize-only；日常直接启动本程序。",
                root.display()
            );
            Ok(())
        })()),
        _ => None,
    }
}

fn secret(root: &Path, name: &str, existing: bool) -> Result<String> {
    if !existing {
        return Ok(format!("{}{}", paths::nonce()?, paths::nonce()?));
    }
    let value = read(&root.join(name))?;
    if value.len() != 64 || !value.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(format!("部署凭据 {name} 无效；请恢复原配置。不会重新生成密码。").into());
    }
    Ok(value)
}

fn read(path: &Path) -> Result<String> {
    paths::ensure_safe_absolute(path)?;
    let file = fs::File::open(path)
        .map_err(|_| format!("部署文件不可读：{}；请恢复原配置。", path.display()))?;
    if !file.metadata()?.is_file() || file.metadata()?.len() > 16 * 1024 {
        return Err("部署文件类型或大小无效。".into());
    }
    let mut value = String::new();
    file.take(16 * 1024 + 1).read_to_string(&mut value)?;
    if value.len() > 16 * 1024 {
        return Err("部署文件过大。".into());
    }
    Ok(value)
}

fn prepare(root: &Path, docker: bool) -> Result<()> {
    paths::ensure_safe_absolute(root)?;
    let (application, maintenance, database) = if docker {
        ("application/", "maintenance/", "postgres/credentials/")
    } else {
        ("", "", "")
    };
    let marker = format!("{application}deployment.json");
    let marker_content = if docker {
        "{\"schemaVersion\":1,\"purpose\":\"exportdoc-image-deployment\"}\n"
    } else {
        "{\"schemaVersion\":1,\"purpose\":\"exportdoc-web-deployment\"}\n"
    };
    let existing = root.join(&marker).try_exists()?;
    if existing {
        if read(&root.join(&marker))? != marker_content {
            return Err("部署标记无效，已保留原配置。".into());
        }
    } else {
        // Docker creates the three empty mount points before invoking setup.
        let directories = if docker {
            vec![
                root.join("application"),
                root.join("maintenance"),
                root.join("postgres"),
            ]
        } else {
            vec![root.to_owned()]
        };
        for directory in &directories {
            paths::ensure_safe_absolute(directory)?;
            if directory.try_exists()? && fs::read_dir(directory)?.next().is_some() {
                return Err(
                    "部署目录已有文件但缺少完整标记。请恢复原配置，不能生成新凭据接管旧数据。"
                        .into(),
                );
            }
        }
        private_directory(root)?;
    }
    let app_name = format!("{database}app_password");
    let maintenance_name = format!("{database}maintenance_password");
    let token_name = format!("{application}bootstrap-token.txt");
    let app = secret(root, &app_name, existing)?;
    let maintenance_password = secret(root, &maintenance_name, existing)?;
    let token = secret(root, &token_name, existing)?;
    let host = if docker { "postgres" } else { "127.0.0.1" };
    let connection = |role: &str, password: &str| {
        format!(
            "host={host} port=5432 dbname=exportdoc_native user={role} password={password} connect_timeout=10 sslmode=disable"
        )
    };
    let mut files = vec![
        (app_name, app.clone()),
        (maintenance_name, maintenance_password.clone()),
        (token_name, token),
        (
            format!("{application}app-connection.txt"),
            connection("exportdoc_app", &app),
        ),
        (
            format!("{maintenance}maintenance-connection.txt"),
            connection("exportdoc_maintenance", &maintenance_password),
        ),
    ];
    if docker {
        let bootstrap_name = format!("{database}bootstrap_password");
        files.push((
            bootstrap_name.clone(),
            secret(root, &bootstrap_name, existing)?,
        ));
        files.push(("postgres/10-native-roles.sh".into(), POSTGRES_INIT.into()));
        files.push((
            "postgres/sql/postgres-roles.sql".into(),
            POSTGRES_ROLES.into(),
        ));
    } else {
        let sql = POSTGRES_ROLES
            .replace(":'app_password'", &format!("'{app}'"))
            .replace(
                ":'maintenance_password'",
                &format!("'{maintenance_password}'"),
            );
        files.push(("initialize.sql".into(), format!("\\set ON_ERROR_STOP on\nCREATE DATABASE exportdoc_native;\n\\connect exportdoc_native\nBEGIN;\n{sql}\nCOMMIT;\n")));
    }
    // Publish the marker only after every file is durable. Partial preparation fails closed.
    files.push((marker, marker_content.into()));
    for (name, value) in files {
        let path = root.join(name);
        paths::ensure_safe_absolute(&path)?;
        if existing {
            if read(&path)? != value {
                return Err(format!("部署文件不匹配：{}；已保留原文件。", path.display()).into());
            }
            continue;
        }
        fs::create_dir_all(path.parent().ok_or("部署路径无效。")?)?;
        let mut options = fs::OpenOptions::new();
        options.create_new(true).write(true);
        #[cfg(unix)]
        {
            use std::os::unix::fs::OpenOptionsExt;
            options.mode(if docker { 0o444 } else { 0o600 });
        }
        let mut file = options.open(&path)?;
        file.write_all(value.as_bytes())?;
        file.sync_all()?;
        #[cfg(unix)]
        if docker {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(&path, fs::Permissions::from_mode(0o444))?;
            fs::set_permissions(path.parent().unwrap(), fs::Permissions::from_mode(0o755))?;
        }
    }
    Ok(())
}

fn prepare_data(path: &Path) -> Result<()> {
    paths::ensure_safe_absolute(path)?;
    fs::create_dir_all(path)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::{MetadataExt, PermissionsExt, chown};
        let owner = fs::metadata(path)?.uid();
        if owner != 10001 && fs::read_dir(path)?.next().is_some() {
            return Err("已有数据目录的所有者须为 UID 10001；请管理员核对挂载目录和权限。".into());
        }
        fs::set_permissions(path, fs::Permissions::from_mode(0o700))?;
        chown(path, Some(10001), Some(10001))?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn configuration_is_repeatable_and_never_replaces_missing_or_changed_credentials() {
        let root =
            Path::new(env!("CARGO_MANIFEST_DIR")).join("../../.codex-runtime/deployment-tests");
        let root = root
            .canonicalize()
            .unwrap_or_else(|_| {
                fs::create_dir_all(&root).unwrap();
                root.canonicalize().unwrap()
            })
            .join(paths::nonce().unwrap());
        for docker in [false, true] {
            let directory = root.join(if docker { "docker" } else { "web" });
            prepare(&directory, docker).unwrap();
            prepare(&directory, docker).unwrap();
            let name = if docker {
                "postgres/credentials/app_password"
            } else {
                "app_password"
            };
            let secret = directory.join(name);
            let original = fs::read(&secret).unwrap();
            fs::remove_file(&secret).unwrap();
            assert!(prepare(&directory, docker).is_err());
            assert!(!secret.exists());
            fs::write(&secret, &original).unwrap();
            let token = directory.join(if docker {
                "application/bootstrap-token.txt"
            } else {
                "bootstrap-token.txt"
            });
            let original_token = fs::read(&token).unwrap();
            fs::remove_file(&secret).unwrap();
            fs::write(&secret, "broken").unwrap();
            assert!(prepare(&directory, docker).is_err());
            assert_eq!(fs::read(&token).unwrap(), original_token);
        }
        fs::remove_dir_all(root).unwrap();
    }
}
