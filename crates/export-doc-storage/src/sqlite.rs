use super::*;
use rusqlite::{
    Connection as SqliteConnection, OptionalExtension,
    backup::{Backup, StepResult},
    params,
};
use serde_json::json;
use std::{
    cell::RefCell,
    time::{Duration, Instant},
};

pub struct Sqlite {
    connection: RefCell<SqliteConnection>,
}
impl From<rusqlite::Error> for Error {
    fn from(error: rusqlite::Error) -> Self {
        if let rusqlite::Error::SqliteFailure(failure, _) = &error {
            if matches!(
                failure.code,
                rusqlite::ErrorCode::DatabaseBusy | rusqlite::ErrorCode::DatabaseLocked
            ) {
                return Self::new(ErrorKind::Busy, "数据库繁忙，请稍后重试。");
            }
            if [1555, 2067, 787].contains(&failure.extended_code) {
                return Self::new(ErrorKind::Conflict, "记录重复或仍被其他业务引用。");
            }
        }
        Self::unavailable(format!("SQLite 存储操作失败：{error}"))
    }
}
impl Sqlite {
    pub fn verify_file(path: &Path) -> Result<()> {
        Self::validate(&SqliteConnection::open_with_flags(
            path,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
        )?)
    }
    pub fn open(path: &Path) -> Result<Self> {
        let fresh = !path.exists();
        let connection = SqliteConnection::open(path)?;
        connection.busy_timeout(Duration::from_secs(5))?;
        connection.pragma_update(None, "foreign_keys", true)?;
        connection.pragma_update(None, "journal_mode", "WAL")?;
        connection.pragma_update(None, "synchronous", "FULL")?;
        if fresh {
            connection.execute_batch(&format!(
                "BEGIN IMMEDIATE;{}COMMIT;",
                include_str!("sqlite.sql")
            ))?;
        }
        Self::validate(&connection)?;
        Ok(Self {
            connection: RefCell::new(connection),
        })
    }
    fn validate(connection: &SqliteConnection) -> Result<()> {
        let (count, version): (i64, Option<i64>) = connection
            .query_row(
                "SELECT COUNT(*), MIN(version) FROM schema_version",
                [],
                |r| Ok((r.get(0)?, r.get(1)?)),
            )
            .map_err(|_| Error::unavailable("数据库缺少有效版本标记，已拒绝启动。"))?;
        if count != 1 || version != Some(SCHEMA_VERSION) {
            return Err(Error::unavailable("数据库版本不受当前 Rust 程序支持。"));
        }
        let integrity: String = connection.query_row("PRAGMA quick_check", [], |r| r.get(0))?;
        if integrity != "ok" {
            return Err(Error::unavailable("数据库完整性检查失败。"));
        }
        connection.prepare("SELECT id,kind,record_id,version,action,actor_id,occurred_at,body FROM audit_logs LIMIT 0")?;
        Ok(())
    }
}
impl Adapter for Sqlite {
    fn provider(&self) -> &'static str {
        "SQLite"
    }
    fn health(&self) -> Result<()> {
        Ok(())
    }
    fn begin(&self) -> Result<()> {
        self.connection.borrow().execute_batch("BEGIN IMMEDIATE;")?;
        Ok(())
    }
    fn commit(&self) -> Result<()> {
        self.connection.borrow().execute_batch("COMMIT;")?;
        Ok(())
    }
    fn rollback(&self) -> Result<()> {
        self.connection.borrow().execute_batch("ROLLBACK;")?;
        Ok(())
    }
    fn checkpoint(&self) -> Result<()> {
        self.connection
            .borrow()
            .execute_batch("PRAGMA wal_checkpoint(TRUNCATE);")?;
        Ok(())
    }
    fn get(&self, kind: &str, id: i64) -> Result<Option<Value>> {
        let body: Option<String> = self
            .connection
            .borrow()
            .query_row(
                "SELECT body FROM records WHERE kind=?1 AND id=?2",
                params![kind, id],
                |r| r.get(0),
            )
            .optional()?;
        body.map(|s| serde_json::from_str(&s).map_err(Into::into))
            .transpose()
    }
    fn all(&self, kind: &str) -> Result<Vec<Value>> {
        let c = self.connection.borrow();
        let mut s = c.prepare("SELECT body FROM records WHERE kind=?1 ORDER BY id DESC")?;
        s.query_map([kind], |r| r.get::<_, String>(0))?
            .map(|r| serde_json::from_str(&r?).map_err(Into::into))
            .collect()
    }
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>> {
        let body: Option<String> = self
            .connection
            .borrow()
            .query_row(
                "SELECT body FROM records WHERE kind=?1 AND identity=?2",
                params![kind, identity],
                |row| row.get(0),
            )
            .optional()?;
        body.map(|body| serde_json::from_str(&body).map_err(Into::into))
            .transpose()
    }
    fn insert(&self, r: &RecordWrite<'_>) -> Result<i64> {
        let c = self.connection.borrow();
        let b = r.body;
        c.execute("INSERT INTO records(kind,identity,version,owner_id,company,department,search_text,body) VALUES(?1,?2,?3,?4,?5,?6,'',?7)",
            params![r.kind,r.identity,b["versionNumber"].as_i64(),b["ownerUserId"].as_i64(),b["companyScope"].as_str().unwrap_or(""),b["departmentId"].as_str().unwrap_or(""),serde_json::to_string(b)?])?;
        Ok(c.last_insert_rowid())
    }
    fn update(&self, id: i64, expected: i64, r: &RecordWrite<'_>) -> Result<bool> {
        let b = r.body;
        Ok(self.connection.borrow().execute("UPDATE records SET identity=?1,version=?2,body=?3,owner_id=?4,company=?5,department=?6 WHERE kind=?7 AND id=?8 AND version=?9",
            params![r.identity,b["versionNumber"].as_i64(),serde_json::to_string(b)?,b["ownerUserId"].as_i64(),b["companyScope"].as_str().unwrap_or(""),b["departmentId"].as_str().unwrap_or(""),r.kind,id,expected])?==1)
    }
    fn set_body(&self, id: i64, body: &Value) -> Result<()> {
        if self.connection.borrow().execute(
            "UPDATE records SET body=?1 WHERE id=?2",
            params![serde_json::to_string(body)?, id],
        )? != 1
        {
            return Err(Error::unavailable("写入记录编号失败。"));
        }
        Ok(())
    }
    fn delete(&self, kind: &str, id: i64, expected: i64) -> Result<bool> {
        Ok(self.connection.borrow().execute(
            "DELETE FROM records WHERE kind=?1 AND id=?2 AND version=?3",
            params![kind, id, expected],
        )? == 1)
    }
    fn append_audit_details(&self, a: &AuditWrite<'_>, details: &Value) -> Result<()> {
        let mut connection = self.connection.borrow_mut();
        let transaction = connection.savepoint()?;
        transaction.execute("INSERT INTO history(kind,record_id,version,action,actor_id,occurred_at,note,body) VALUES(?1,?2,?3,?4,?5,?6,?7,'{}')",
            params![a.kind,a.record_id,a.version,a.action,a.actor_id,a.occurred_at,a.note])?;
        transaction.execute("INSERT INTO audit_logs(kind,record_id,version,action,actor_id,occurred_at,body) VALUES(?1,?2,?3,?4,?5,?6,?7)",
            params![a.kind,a.record_id,a.version,a.action,a.actor_id,a.occurred_at,serde_json::to_string(details)?])?;
        transaction.commit()?;
        Ok(())
    }
    fn audit_events(&self, before_id: i64, limit: i64) -> Result<Vec<Value>> {
        let connection = self.connection.borrow();
        let mut statement = connection.prepare("SELECT id,kind,record_id,version,action,actor_id,occurred_at,body FROM audit_logs WHERE id<?1 ORDER BY id DESC LIMIT ?2")?;
        statement.query_map(params![before_id,limit.clamp(1,1000)], |row| {
            Ok((row.get::<_,i64>(0)?,row.get::<_,String>(1)?,row.get::<_,i64>(2)?,row.get::<_,i64>(3)?,
                row.get::<_,String>(4)?,row.get::<_,i64>(5)?,row.get::<_,String>(6)?,row.get::<_,String>(7)?))
        })?.map(|row| {
            let (id,kind,record,version,action,actor,time,body) = row?;
            let body: Value = serde_json::from_str(&body)?;
            Ok(json!({"id":id,"kind":kind,"recordId":record,"versionNumber":version,"action":action,"actorUserId":actor,"timestamp":time,"body":body}))
        }).collect()
    }
    fn delete_audits(&self, ids: &[i64]) -> Result<usize> {
        let connection = self.connection.borrow();
        let mut count = 0;
        for chunk in ids.chunks(256) {
            let placeholders = vec!["?"; chunk.len()].join(",");
            count += connection.execute(
                &format!("DELETE FROM audit_logs WHERE id IN ({placeholders})"),
                rusqlite::params_from_iter(chunk),
            )?;
        }
        Ok(count)
    }
    fn history(&self, kind: Option<&str>, id: Option<i64>) -> Result<Vec<Value>> {
        let c = self.connection.borrow();
        let mut s=c.prepare("SELECT id,kind,record_id,version,action,actor_id,occurred_at,note FROM history WHERE (?1 IS NULL OR kind=?1) AND (?2 IS NULL OR record_id=?2) ORDER BY id DESC LIMIT 10000")?;
        s.query_map(params![kind,id],|r|Ok(json!({"id":r.get::<_,i64>(0)?,"module":r.get::<_,String>(1)?,"recordId":r.get::<_,i64>(2)?,"versionNumber":r.get::<_,i64>(3)?,"action":r.get::<_,String>(4)?,"actorUserId":r.get::<_,i64>(5)?,"createdAt":r.get::<_,String>(6)?,"timestamp":r.get::<_,String>(6)?,"note":r.get::<_,String>(7)?,"description":r.get::<_,String>(7)?})))?.map(|r|r.map_err(Into::into)).collect()
    }
    fn credential(&self, id: i64) -> Result<Option<Credential>> {
        Ok(self
            .connection
            .borrow()
            .query_row(
                "SELECT salt,password_hash,iterations FROM credentials WHERE user_id=?1",
                [id],
                |r| {
                    Ok(Credential {
                        salt: r.get(0)?,
                        hash: r.get(1)?,
                        iterations: r.get(2)?,
                    })
                },
            )
            .optional()?)
    }
    fn set_credential(&self, id: i64, salt: &[u8], hash: &[u8], iterations: u32) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO credentials(user_id,salt,password_hash,iterations) VALUES(?1,?2,?3,?4) ON CONFLICT(user_id) DO UPDATE SET salt=excluded.salt,password_hash=excluded.password_hash,iterations=excluded.iterations",params![id,salt,hash,iterations])?;
        Ok(())
    }
    fn settings(&self, name: &str) -> Result<Option<Value>> {
        let value: Option<String> = self
            .connection
            .borrow()
            .query_row("SELECT body FROM settings WHERE name=?1", [name], |r| {
                r.get(0)
            })
            .optional()?;
        value
            .map(|v| serde_json::from_str(&v).map_err(Into::into))
            .transpose()
    }
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO settings(name,version,body) VALUES(?1,?2,?3) ON CONFLICT(name) DO UPDATE SET version=excluded.version,body=excluded.body",params![name,version,serde_json::to_string(body)?])?;
        Ok(())
    }
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64> {
        Ok(self.connection.borrow().query_row("SELECT COALESCE(SUM(length(f.content)),0) FROM files f JOIN records r ON r.id=f.record_id WHERE r.kind='attachments' AND json_extract(r.body,'$.invoiceId')=?1",[invoice_id],|r|r.get(0))?)
    }
    fn insert_blob(&self, b: &BlobWrite<'_>) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO files(kind,record_id,file_name,media_type,digest,content,created_at) VALUES(?1,?2,?3,?4,?5,?6,?7)",params![b.kind,b.record_id,b.file_name,b.media_type,b.digest,b.content,b.created_at])?;
        Ok(())
    }
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>> {
        Ok(self.connection.borrow().query_row("SELECT file_name,media_type,digest,content FROM files WHERE record_id=?1 AND kind=?2 ORDER BY id DESC LIMIT 1",params![record_id,kind],|r|Ok(Blob{file_name:r.get(0)?,media_type:r.get(1)?,digest:r.get(2)?,content:r.get(3)?})).optional()?)
    }
    fn delete_blobs(&self, record_id: i64) -> Result<()> {
        self.connection
            .borrow()
            .execute("DELETE FROM files WHERE record_id=?1", [record_id])?;
        Ok(())
    }
    fn delete_blob(&self, record_id: i64, kind: &str) -> Result<()> {
        self.connection.borrow().execute(
            "DELETE FROM files WHERE record_id=?1 AND kind=?2",
            params![record_id, kind],
        )?;
        Ok(())
    }
    fn backup(&self, path: &Path) -> Result<()> {
        let source = self.connection.borrow();
        let mut target = SqliteConnection::open(path)?;
        copy(&source, &mut target)?;
        Self::validate(&target)
    }
    fn restore(&self, path: &Path) -> Result<()> {
        let source =
            SqliteConnection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        Self::validate(&source)?;
        let mut destination = self.connection.borrow_mut();
        copy(&source, &mut destination)?;
        Self::validate(&destination)
    }
    fn verify_backup(&self, path: &Path) -> Result<()> {
        let source =
            SqliteConnection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        Self::validate(&source)
    }
}
fn copy(source: &SqliteConnection, destination: &mut SqliteConnection) -> Result<()> {
    let backup = Backup::new(source, destination)?;
    let deadline = Instant::now() + Duration::from_secs(120);
    loop {
        if Instant::now() > deadline {
            return Err(Error::new(ErrorKind::Timeout, "数据库备份 / 恢复超时。"));
        }
        match backup.step(256)? {
            StepResult::Done => break,
            StepResult::More => {}
            StepResult::Busy | StepResult::Locked => std::thread::sleep(Duration::from_millis(20)),
            _ => {}
        }
    }
    Ok(())
}
