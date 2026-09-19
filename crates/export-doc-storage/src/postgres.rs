use super::*;
use ::postgres::{Client, Config};
use postgres_native_tls::MakeTlsConnector;
use serde_json::json;
use std::{
    cell::RefCell,
    time::{Duration, Instant},
};

pub struct Postgres {
    client: RefCell<Client>,
    lock: RefCell<(Client, Instant)>,
}
const INSTANCE_LOCK: i64 = 0x455850444F434D47;
impl From<::postgres::Error> for Error {
    fn from(error: ::postgres::Error) -> Self {
        if let Some(db) = error.as_db_error() {
            let kind = match db.code().code() {
                "23505" | "23503" | "40001" => ErrorKind::Conflict,
                "55P03" => ErrorKind::Busy,
                "57014" => ErrorKind::Timeout,
                _ => ErrorKind::Unavailable,
            };
            let message = match kind {
                ErrorKind::Conflict => "记录重复、被引用或事务并发冲突。",
                ErrorKind::Busy => "PostgreSQL 正在处理其他请求。",
                ErrorKind::Timeout => "PostgreSQL 操作超过时限。",
                _ => "PostgreSQL 数据库操作失败。",
            };
            return Self::new(kind, format!("{message} SQLSTATE={}", db.code().code()));
        }
        Self::unavailable("PostgreSQL 连接或协议故障，已停止本次操作。")
    }
}
impl Postgres {
    pub fn open(connection_string: &str) -> Result<Self> {
        let mut lock = connect(connection_string)?;
        acquire_lock(&mut lock)?;
        let mut client = connect(connection_string)?;
        let privileges = client.query_one(
            "SELECT rolsuper, rolcreatedb, rolcreaterole, has_schema_privilege(current_user, current_schema(), 'CREATE') FROM pg_roles WHERE rolname=current_user", &[])?;
        if (0..4).any(|column| privileges.get::<_, bool>(column)) {
            return Err(Error::unavailable(
                "普通 API 必须使用没有管理或建表权限的业务账号。",
            ));
        }
        validate_schema(&mut client)?;
        Ok(Self {
            client: RefCell::new(client),
            lock: RefCell::new((lock, Instant::now())),
        })
    }
}

fn connect(connection_string: &str) -> Result<Client> {
    let mut config: Config = connection_string
        .parse()
        .map_err(|_| Error::unavailable("PostgreSQL 连接配置无效。"))?;
    config.connect_timeout(Duration::from_secs(10));
    config.application_name("ExportDocManager.Rust");
    config.options("-c statement_timeout=25000 -c lock_timeout=5000 -c idle_in_transaction_session_timeout=30000 -c timezone=UTC -c search_path=public");
    let tls = native_tls::TlsConnector::builder()
        .build()
        .map_err(|_| Error::unavailable("无法初始化系统 TLS。"))?;
    let mut client = config.connect(MakeTlsConnector::new(tls))?;
    let version: String = client
        .query_one("SHOW server_version_num", &[])?
        .try_get(0)?;
    let version: u32 = version
        .parse()
        .map_err(|_| Error::unavailable("无法读取 PostgreSQL 版本。"))?;
    if !(180000..190000).contains(&version) {
        return Err(Error::unavailable("团队数据库必须使用 PostgreSQL 18。"));
    }
    Ok(client)
}

fn acquire_lock(client: &mut Client) -> Result<()> {
    // PostgreSQL advisory locks are scoped to the current database; this key
    // identifies the ExportDocManager instance within that database.
    let acquired: bool = client
        .query_one("SELECT pg_try_advisory_lock($1)", &[&INSTANCE_LOCK])?
        .try_get(0)?;
    if !acquired {
        return Err(Error::new(
            ErrorKind::Busy,
            "该数据库已由另一个 API 实例使用。",
        ));
    }
    Ok(())
}

fn validate_schema(client: &mut Client) -> Result<()> {
    let rows = client.query("SELECT version FROM schema_version", &[])?;
    if rows.len() != 1 || rows[0].try_get::<_, i64>(0)? != super::SCHEMA_VERSION {
        return Err(Error::unavailable(
            "PostgreSQL 数据库不是当前 Rust schema 基线。",
        ));
    }
    Ok(())
}

pub fn initialize(connection_string: &str, owner: &str) -> Result<()> {
    if owner.is_empty()
        || owner.len() > 63
        || !owner
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'_')
    {
        return Err(Error::unavailable("数据库所有者必须是有效的独立角色名。"));
    }
    let mut client = connect(connection_string)?;
    acquire_lock(&mut client)?;
    let count: i64 = client
        .query_one(
            "SELECT count(*) FROM pg_tables WHERE schemaname='public'",
            &[],
        )?
        .try_get(0)?;
    if count != 0 {
        return validate_schema(&mut client);
    }
    let role = client.query_opt("SELECT rolcanlogin, rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname=$1", &[&owner])?
        .ok_or_else(|| Error::unavailable("请先创建独立的 NOLOGIN 数据库所有者角色。"))?;
    if (0..4).any(|column| role.get::<_, bool>(column)) {
        return Err(Error::unavailable(
            "数据库所有者必须是 NOLOGIN 且不具备集群管理权限。",
        ));
    }
    let mut transaction = client.transaction()?;
    transaction.batch_execute(&format!("SET LOCAL ROLE \"{owner}\""))?;
    transaction.batch_execute(include_str!("postgres.sql"))?;
    transaction.commit()?;
    validate_schema(&mut client)
}
impl Adapter for Postgres {
    fn provider(&self) -> &'static str {
        "PostgreSQL"
    }
    fn health(&self) -> Result<()> {
        let mut lock = self.lock.borrow_mut();
        if lock.0.is_closed() || self.client.borrow().is_closed() {
            return Err(Error::unavailable(
                "PostgreSQL 实例锁连接已中断，必须重新启动服务。",
            ));
        }
        if lock.1.elapsed() > Duration::from_secs(2) {
            lock.0.simple_query("SELECT 1")?;
            lock.1 = Instant::now();
        }
        Ok(())
    }
    fn begin(&self) -> Result<()> {
        self.client
            .borrow_mut()
            .batch_execute("BEGIN ISOLATION LEVEL SERIALIZABLE")?;
        Ok(())
    }
    fn commit(&self) -> Result<()> {
        self.client.borrow_mut().batch_execute("COMMIT")?;
        Ok(())
    }
    fn rollback(&self) -> Result<()> {
        self.client.borrow_mut().batch_execute("ROLLBACK")?;
        Ok(())
    }
    fn checkpoint(&self) -> Result<()> {
        self.health()
    }
    fn get(&self, kind: &str, id: i64) -> Result<Option<Value>> {
        self.client
            .borrow_mut()
            .query_opt(
                "SELECT body::text FROM records WHERE kind=$1 AND id=$2",
                &[&kind, &id],
            )?
            .map(|row| {
                let text: String = row.try_get(0)?;
                serde_json::from_str(&text).map_err(Into::into)
            })
            .transpose()
    }
    fn all(&self, kind: &str) -> Result<Vec<Value>> {
        self.client
            .borrow_mut()
            .query(
                "SELECT body::text FROM records WHERE kind=$1 ORDER BY id DESC",
                &[&kind],
            )?
            .into_iter()
            .map(|row| {
                let text: String = row.try_get(0)?;
                serde_json::from_str(&text).map_err(Into::into)
            })
            .collect()
    }
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>> {
        self.client
            .borrow_mut()
            .query_opt(
                "SELECT body::text FROM records WHERE kind=$1 AND identity=$2",
                &[&kind, &identity],
            )?
            .map(|row| {
                let body: String = row.try_get(0)?;
                serde_json::from_str(&body).map_err(Into::into)
            })
            .transpose()
    }
    fn insert(&self, r: &RecordWrite<'_>) -> Result<i64> {
        let b = r.body;
        let body = serde_json::to_string(b)?;
        Ok(self.client.borrow_mut().query_one("INSERT INTO records(kind,identity,version,owner_id,company,department,body) VALUES($1,$2,$3,$4,$5,$6,$7::text::jsonb) RETURNING id",
            &[&r.kind,&r.identity,&b["versionNumber"].as_i64(),&b["ownerUserId"].as_i64(),&b["companyScope"].as_str().unwrap_or(""),&b["departmentId"].as_str().unwrap_or(""),&body])?.try_get(0)?)
    }
    fn update(&self, id: i64, expected: i64, r: &RecordWrite<'_>) -> Result<bool> {
        let b = r.body;
        let body = serde_json::to_string(b)?;
        Ok(self.client.borrow_mut().execute("UPDATE records SET identity=$1,version=$2,body=$3::text::jsonb,owner_id=$4,company=$5,department=$6 WHERE kind=$7 AND id=$8 AND version=$9",
            &[&r.identity,&b["versionNumber"].as_i64(),&body,&b["ownerUserId"].as_i64(),&b["companyScope"].as_str().unwrap_or(""),&b["departmentId"].as_str().unwrap_or(""),&r.kind,&id,&expected])?==1)
    }
    fn set_body(&self, id: i64, body: &Value) -> Result<()> {
        if self.client.borrow_mut().execute(
            "UPDATE records SET body=$1::text::jsonb WHERE id=$2",
            &[&serde_json::to_string(body)?, &id],
        )? != 1
        {
            return Err(Error::unavailable("写入记录编号失败。"));
        }
        Ok(())
    }
    fn delete(&self, kind: &str, id: i64, expected: i64) -> Result<bool> {
        Ok(self.client.borrow_mut().execute(
            "DELETE FROM records WHERE kind=$1 AND id=$2 AND version=$3",
            &[&kind, &id, &expected],
        )? == 1)
    }
    fn append_audit_details(&self, a: &AuditWrite<'_>, details: &Value) -> Result<()> {
        self.client.borrow_mut().execute("WITH event AS (INSERT INTO history(kind,record_id,version,action,actor_id,occurred_at,note,body) VALUES($1,$2,$3,$4,$5,$6,$7,'{}') RETURNING kind,record_id,version,action,actor_id,occurred_at) INSERT INTO audit_logs(kind,record_id,version,action,actor_id,occurred_at,body) SELECT kind,record_id,version,action,actor_id,occurred_at,$8::text::jsonb FROM event",
            &[&a.kind,&a.record_id,&a.version,&a.action,&a.actor_id,&a.occurred_at,&a.note,&serde_json::to_string(details)?])?;
        Ok(())
    }
    fn audit_events(&self, before_id: i64, limit: i64) -> Result<Vec<Value>> {
        self.client.borrow_mut().query("SELECT id,kind,record_id,version,action,actor_id,occurred_at,body::text FROM audit_logs WHERE id<$1 ORDER BY id DESC LIMIT $2",
            &[&before_id,&limit.clamp(1,1000)])?.into_iter().map(|row| {
            let body: Value = serde_json::from_str(&row.try_get::<_,String>(7)?)?;
            Ok(json!({"id":row.try_get::<_,i64>(0)?,"kind":row.try_get::<_,String>(1)?,
                "recordId":row.try_get::<_,i64>(2)?,"versionNumber":row.try_get::<_,i64>(3)?,
                "action":row.try_get::<_,String>(4)?,"actorUserId":row.try_get::<_,i64>(5)?,
                "timestamp":row.try_get::<_,String>(6)?,"body":body}))
        }).collect()
    }
    fn delete_audits(&self, ids: &[i64]) -> Result<usize> {
        Ok(self
            .client
            .borrow_mut()
            .execute("DELETE FROM audit_logs WHERE id=ANY($1)", &[&ids])? as usize)
    }
    fn history(&self, kind: Option<&str>, id: Option<i64>) -> Result<Vec<Value>> {
        self.client.borrow_mut().query("SELECT id,kind,record_id,version,action,actor_id,occurred_at,note FROM history WHERE ($1::text IS NULL OR kind=$1) AND ($2::bigint IS NULL OR record_id=$2) ORDER BY id DESC LIMIT 10000",&[&kind,&id])?.into_iter().map(|r|Ok(json!({"id":r.try_get::<_,i64>(0)?,"module":r.try_get::<_,String>(1)?,"recordId":r.try_get::<_,i64>(2)?,"versionNumber":r.try_get::<_,i64>(3)?,"action":r.try_get::<_,String>(4)?,"actorUserId":r.try_get::<_,i64>(5)?,"createdAt":r.try_get::<_,String>(6)?,"timestamp":r.try_get::<_,String>(6)?,"note":r.try_get::<_,String>(7)?,"description":r.try_get::<_,String>(7)?}))).collect()
    }
    fn credential(&self, id: i64) -> Result<Option<Credential>> {
        self.client
            .borrow_mut()
            .query_opt(
                "SELECT salt,password_hash,iterations FROM credentials WHERE user_id=$1",
                &[&id],
            )?
            .map(|r| {
                Ok(Credential {
                    salt: r.try_get(0)?,
                    hash: r.try_get(1)?,
                    iterations: u32::try_from(r.try_get::<_, i64>(2)?)
                        .map_err(|_| Error::unavailable("凭证参数无效。"))?,
                })
            })
            .transpose()
    }
    fn set_credential(&self, id: i64, salt: &[u8], hash: &[u8], iterations: u32) -> Result<()> {
        self.client.borrow_mut().execute("INSERT INTO credentials(user_id,salt,password_hash,iterations) VALUES($1,$2,$3,$4) ON CONFLICT(user_id) DO UPDATE SET salt=excluded.salt,password_hash=excluded.password_hash,iterations=excluded.iterations",&[&id,&salt,&hash,&i64::from(iterations)])?;
        Ok(())
    }
    fn settings(&self, name: &str) -> Result<Option<Value>> {
        self.client
            .borrow_mut()
            .query_opt("SELECT body::text FROM settings WHERE name=$1", &[&name])?
            .map(|r| {
                let body: String = r.try_get(0)?;
                serde_json::from_str(&body).map_err(Into::into)
            })
            .transpose()
    }
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()> {
        self.client.borrow_mut().execute("INSERT INTO settings(name,version,body) VALUES($1,$2,$3::text::jsonb) ON CONFLICT(name) DO UPDATE SET version=excluded.version,body=excluded.body",&[&name,&version,&serde_json::to_string(body)?])?;
        Ok(())
    }
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64> {
        Ok(self.client.borrow_mut().query_one("SELECT COALESCE(SUM(octet_length(f.content)),0)::bigint FROM files f JOIN records r ON r.id=f.record_id WHERE r.kind='attachments' AND (r.body->>'invoiceId')::bigint=$1",&[&invoice_id])?.try_get(0)?)
    }
    fn insert_blob(&self, b: &BlobWrite<'_>) -> Result<()> {
        self.client.borrow_mut().execute("INSERT INTO files(kind,record_id,file_name,media_type,digest,content,created_at) VALUES($1,$2,$3,$4,$5,$6,$7)",&[&b.kind,&b.record_id,&b.file_name,&b.media_type,&b.digest,&b.content,&b.created_at])?;
        Ok(())
    }
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>> {
        self.client.borrow_mut().query_opt("SELECT file_name,media_type,digest,content FROM files WHERE record_id=$1 AND kind=$2 ORDER BY id DESC LIMIT 1",&[&record_id,&kind])?.map(|r|Ok(Blob{file_name:r.try_get(0)?,media_type:r.try_get(1)?,digest:r.try_get(2)?,content:r.try_get(3)?})).transpose()
    }
    fn delete_blobs(&self, record_id: i64) -> Result<()> {
        self.client
            .borrow_mut()
            .execute("DELETE FROM files WHERE record_id=$1", &[&record_id])?;
        Ok(())
    }
    fn delete_blob(&self, record_id: i64, kind: &str) -> Result<()> {
        self.client.borrow_mut().execute(
            "DELETE FROM files WHERE record_id=$1 AND kind=$2",
            &[&record_id, &kind],
        )?;
        Ok(())
    }
}
