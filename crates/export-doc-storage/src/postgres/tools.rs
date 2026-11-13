//! The Rust connection and PostgreSQL client tools share one parser.
use super::*;

pub struct ClientParameters {
    pub host: String,
    pub port: String,
    pub database: String,
    pub username: String,
    pub password: String,
    pub ssl_mode: &'static str,
}

pub fn client_parameters(connection: &str) -> Result<ClientParameters> {
    let config: Config = connection
        .parse()
        .map_err(|_| Error::unavailable("PostgreSQL 连接配置无效。"))?;
    let hosts: Vec<String> = config
        .get_hosts()
        .iter()
        .map(|host| match host {
            ::tokio_postgres::config::Host::Tcp(host) => host.clone(),
            #[cfg(unix)]
            ::tokio_postgres::config::Host::Unix(path) => path.to_string_lossy().into_owned(),
        })
        .collect();
    let database = config.get_dbname().unwrap_or("").to_owned();
    if hosts.is_empty() || database.is_empty() || database.contains('=') || database.contains("://")
    {
        return Err(Error::unavailable(
            "PostgreSQL 客户端需要明确的主机和数据库名。",
        ));
    }
    let ports: Vec<_> = config.get_ports().iter().map(u16::to_string).collect();
    Ok(ClientParameters {
        host: hosts.join(","),
        port: if ports.is_empty() {
            "5432".into()
        } else {
            ports.join(",")
        },
        database,
        username: config.get_user().unwrap_or("").to_owned(),
        password: String::from_utf8(config.get_password().unwrap_or_default().to_vec())
            .map_err(|_| Error::unavailable("PostgreSQL 密码必须为 UTF-8。"))?,
        ssl_mode: match config.get_ssl_mode() {
            ::tokio_postgres::config::SslMode::Disable => "disable",
            ::tokio_postgres::config::SslMode::Require => "verify-full",
            _ => "prefer",
        },
    })
}

pub struct MaintenanceLease {
    client: Client,
}
impl MaintenanceLease {
    pub fn acquire(connection: &str, owner: &str) -> Result<Self> {
        crate::postgres_privileges::role_name(owner)?;
        let mut client = connect(connection)?;
        acquire_lock(&mut client)?;
        let valid: bool = client.query_one("SELECT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=$1 AND NOT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole) AND pg_has_role(current_user,$1,'MEMBER')", &[&owner])?.try_get(0)?;
        if !valid {
            return Err(Error::unavailable(
                "恢复需要具有目标 NOLOGIN 所有者角色成员资格的维护账号。",
            ));
        }
        Ok(Self { client })
    }
    pub fn validate(&mut self) -> Result<()> {
        validate_schema(&mut self.client)
    }
    pub fn file_objects(&mut self, after: &str) -> Result<Vec<crate::FileObject>> {
        self.client.query("SELECT digest,size_bytes FROM file_objects WHERE digest>$1 ORDER BY digest LIMIT 200", &[&after])?
            .into_iter().map(|row| Ok(crate::FileObject { digest: row.try_get(0)?, size: u64::try_from(row.try_get::<_,i64>(1)?).map_err(|_| Error::unavailable("文件对象长度无效。"))? })).collect()
    }
    pub fn finish(&mut self, owner: &str, application: &str) -> Result<()> {
        crate::postgres_privileges::role_name(owner)?;
        let grants = crate::postgres_privileges::grants(application)?;
        let valid: bool = self.client.query_one("SELECT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=$1 AND rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole) AND NOT pg_has_role($1,$2,'MEMBER')", &[&application,&owner])?.try_get(0)?;
        if !valid {
            return Err(Error::unavailable(
                "恢复后的业务账号必须独立且没有维护权限。",
            ));
        }
        let mut transaction = self.client.transaction()?;
        transaction.batch_execute(&format!("SET LOCAL ROLE \"{owner}\";\n{grants}"))?;
        super::validate_schema(&mut transaction)?;
        transaction.commit()?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn client_parameters_keep_quoted_passwords_and_url_decoding() {
        let keyword = client_parameters(
            "host=127.0.0.1 port=5544 user=app dbname=business password='a b\\'c' sslmode=disable",
        )
        .unwrap();
        assert_eq!(keyword.password, "a b'c");
        assert_eq!(keyword.ssl_mode, "disable");
        let url =
            client_parameters("postgresql://app:a%40b%3Ac@[::1]:5544/business?sslmode=require")
                .unwrap();
        assert_eq!(url.host, "::1");
        assert_eq!(url.password, "a@b:c");
        assert_eq!(url.ssl_mode, "verify-full");
    }
}
