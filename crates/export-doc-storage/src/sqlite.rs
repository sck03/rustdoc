use super::*;
#[cfg(test)]
mod tests;
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
// Relational projections are compiled once per connection. Bound parameters
// remain per-call, and rusqlite invalidates statements when the schema changes.
trait CachedQuery {
    fn query_cached<T, P, F>(&self, sql: &str, values: P, map: F) -> rusqlite::Result<T>
    where
        P: rusqlite::Params,
        F: FnOnce(&rusqlite::Row<'_>) -> rusqlite::Result<T>;
}
impl CachedQuery for SqliteConnection {
    fn query_cached<T, P, F>(&self, sql: &str, values: P, map: F) -> rusqlite::Result<T>
    where
        P: rusqlite::Params,
        F: FnOnce(&rusqlite::Row<'_>) -> rusqlite::Result<T>,
    {
        self.prepare_cached(sql)?.query_row(values, map)
    }
}
impl From<rusqlite::Error> for Error {
    fn from(error: rusqlite::Error) -> Self {
        if let rusqlite::Error::SqliteFailure(failure, _) = &error {
            if failure.code == rusqlite::ErrorCode::OperationInterrupted {
                return Self::new(ErrorKind::Timeout, "SQLite 操作已取消或超过时限。");
            }
            if matches!(
                failure.code,
                rusqlite::ErrorCode::DatabaseBusy | rusqlite::ErrorCode::DatabaseLocked
            ) {
                return Self::new(ErrorKind::Busy, "数据库繁忙，请稍后重试。");
            }
            if [1555, 2067, 787, 1811].contains(&failure.extended_code) {
                return Self::new(ErrorKind::Conflict, "记录重复或仍被其他业务引用。");
            }
        }
        Self::unavailable(format!("SQLite 存储操作失败：{error}"))
    }
}
impl Sqlite {
    pub fn prepare_restore_file(source: &Path, destination: &Path) -> Result<()> {
        let source =
            SqliteConnection::open_with_flags(source, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        Self::validate(&source)?;
        // The caller supplies a managed scratch path; never overwrite a backup or business file.
        drop(
            std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(destination)?,
        );
        {
            let mut target = SqliteConnection::open(destination)?;
            copy(&source, &mut target)?;
        }
        Self::open(destination)?.checkpoint()?;
        Ok(())
    }
    pub fn verify_file(path: &Path) -> Result<()> {
        Self::validate(&SqliteConnection::open_with_flags(
            path,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
        )?)
    }
    pub fn open(path: &Path) -> Result<Self> {
        let fresh = !path.try_exists()?;
        let mut connection = SqliteConnection::open(path)?;
        connection.set_prepared_statement_cache_capacity(128);
        connection.progress_handler(1000, Some(operation::cancelled as fn() -> bool))?;
        connection.busy_timeout(Duration::from_secs(5))?;
        connection.pragma_update(None, "foreign_keys", true)?;
        // Reject invalid existing files before changing their journal mode. Configure WAL before
        // migration DDL so the same connection can checkpoint immediately after the commit.
        if !fresh {
            Self::validate(&connection)?;
        }
        connection.pragma_update(None, "journal_mode", "WAL")?;
        connection.pragma_update(None, "synchronous", "FULL")?;
        let transaction =
            connection.transaction_with_behavior(rusqlite::TransactionBehavior::Immediate)?;
        if fresh {
            transaction.execute_batch(include_str!("sqlite.sql"))?;
            transaction.execute_batch(include_str!("permission_versions_sqlite.sql"))?;
            transaction.execute_batch(include_str!("identity_runtime_sqlite.sql"))?;
            transaction.execute_batch(include_str!("personnel_foundation_sqlite.sql"))?;
            transaction.execute_batch(include_str!("access_scope_sqlite.sql"))?;
            transaction.execute_batch(include_str!("workflow_runtime_sqlite.sql"))?;
            transaction.execute(
                "INSERT INTO schema_model(version,model,shape) VALUES(?1,?2,?3)",
                params![
                    schema::SCHEMA_VERSION,
                    schema::model_fingerprint(false),
                    schema::sqlite_shape(&transaction)?
                ],
            )?;
        }
        if fresh {
            Self::validate(&transaction)?;
        } else {
            Self::validate_structure(&transaction)?;
        }
        transaction.commit()?;
        connection.create_scalar_function(
            "template_normalize",
            1,
            rusqlite::functions::FunctionFlags::SQLITE_UTF8
                | rusqlite::functions::FunctionFlags::SQLITE_DETERMINISTIC,
            |context| {
                use unicode_normalization::UnicodeNormalization;
                Ok(context
                    .get::<Option<String>>(0)?
                    .unwrap_or_default()
                    .nfc()
                    .collect::<String>()
                    .trim()
                    .to_lowercase())
            },
        )?;
        connection.create_scalar_function(
            "record_json",
            1,
            rusqlite::functions::FunctionFlags::SQLITE_UTF8
                | rusqlite::functions::FunctionFlags::SQLITE_DETERMINISTIC,
            |context| {
                let source = context
                    .get::<Option<String>>(0)?
                    .unwrap_or_else(|| "null".into());
                let value: Value = serde_json::from_str(&source)
                    .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))?;
                serde_json::to_string(&value)
                    .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))
            },
        )?;
        connection.create_scalar_function(
            "record_date",
            1,
            rusqlite::functions::FunctionFlags::SQLITE_UTF8
                | rusqlite::functions::FunctionFlags::SQLITE_DETERMINISTIC,
            |context| {
                let value = context.get::<Option<String>>(0)?.unwrap_or_default();
                let date = chrono::NaiveDate::parse_from_str(&value, "%Y-%m-%d")
                    .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))?;
                if date.format("%Y-%m-%d").to_string() != value {
                    return Err(rusqlite::Error::UserFunctionError(
                        "Invalid business date".into(),
                    ));
                }
                Ok(value)
            },
        )?;
        connection.create_scalar_function(
            "job_time",
            1,
            rusqlite::functions::FunctionFlags::SQLITE_UTF8
                | rusqlite::functions::FunctionFlags::SQLITE_DETERMINISTIC,
            |context| {
                let value = context.get::<String>(0)?;
                chrono::DateTime::parse_from_rfc3339(&value)
                    .map(|time| time.timestamp_millis())
                    .map_err(|e| rusqlite::Error::UserFunctionError(Box::new(e)))
            },
        )?;
        for (name, seconds) in [("record_time_seconds", true), ("record_time_nanos", false)] {
            connection.create_scalar_function(
                name,
                1,
                rusqlite::functions::FunctionFlags::SQLITE_UTF8
                    | rusqlite::functions::FunctionFlags::SQLITE_DETERMINISTIC,
                move |context| {
                    let value = context.get::<String>(0)?;
                    chrono::DateTime::parse_from_rfc3339(&value)
                        .map(|time| {
                            if seconds {
                                time.timestamp()
                            } else {
                                i64::from(time.timestamp_subsec_nanos())
                            }
                        })
                        .map_err(|cause| rusqlite::Error::UserFunctionError(Box::new(cause)))
                },
            )?;
        }
        Ok(Self {
            connection: RefCell::new(connection),
        })
    }
    fn version(connection: &SqliteConnection) -> Result<i64> {
        let (count, version): (i64, Option<i64>) = connection
            .query_cached(
                "SELECT COUNT(*), MIN(version) FROM schema_version",
                [],
                |r| Ok((r.get(0)?, r.get(1)?)),
            )
            .map_err(|_| Error::unavailable("数据库缺少有效版本标记，已拒绝启动。"))?;
        if count != 1 || version.is_none() {
            return Err(Error::unavailable(
                "数据库版本标记必须唯一且有效，已保留原库并拒绝启动。",
            ));
        }
        Ok(version.expect("validated marker"))
    }
    fn validate(connection: &SqliteConnection) -> Result<()> {
        operation::check()?;
        connection.progress_handler(1000, Some(operation::cancelled as fn() -> bool))?;
        Self::validate_structure(connection)?;
        let integrity: String = connection.query_cached("PRAGMA quick_check", [], |r| r.get(0))?;
        if integrity != "ok" {
            return Err(Error::unavailable("数据库完整性检查失败。"));
        }
        Ok(())
    }
    fn validate_structure(connection: &SqliteConnection) -> Result<()> {
        schema::validate(Self::version(connection)?)?;
        let (model, shape): (String, String) = connection.query_cached(
            "SELECT model,shape FROM schema_model WHERE version=?1",
            [schema::SCHEMA_VERSION],
            |r| Ok((r.get(0)?, r.get(1)?)),
        )?;
        schema::validate_model(false, &model, &shape, &schema::sqlite_shape(connection)?)?;
        connection.prepare_cached("SELECT id,request_id,revision,state,generation,definition FROM workflow_instances LIMIT 0")?;
        connection
            .prepare_cached("SELECT id,request_id,revision,body FROM request_revisions LIMIT 0")?;
        connection.prepare_cached("SELECT instance_id,generation,node_key,position,definition,started_at,due_at FROM node_instances LIMIT 0")?;
        connection.prepare_cached("SELECT actor_id,context_id,command_key,request_digest,result FROM command_receipts LIMIT 0")?;
        connection.prepare_cached("SELECT id,kind,body FROM record_rows LIMIT 0")?;
        connection.prepare_cached("SELECT request_id,submission,task_key,node_key,assignee_id,acted_by_id,context_id,acted_by_context,status,active,due_at,independent,started_at,acted_at,elapsed_seconds FROM workflow_tasks LIMIT 0")?;
        connection.prepare_cached("SELECT id,person_id,unit_id,department_id,company,department,job_title,starts_on,ends_on,is_primary FROM personnel_memberships LIMIT 0")?;
        connection.prepare_cached("SELECT appointment_id,manager_appointment_id,relation_type FROM reporting_relations LIMIT 0")?;
        connection
            .prepare_cached("SELECT id,code,parent_id,unit_kind FROM organization_units LIMIT 0")?;
        connection.prepare_cached(
            "SELECT id,unit_id,code,parent_id FROM organization_departments LIMIT 0",
        )?;
        connection.prepare_cached("SELECT person_id,user_id FROM personnel_accounts LIMIT 0")?;
        connection.prepare_cached("SELECT organization_id,appointment_id,duty_kind,workflow_kinds FROM organization_duties LIMIT 0")?;
        connection.prepare_cached("SELECT id,user_id,unit_id,appointment_id,mode,is_active,template_id,authority_role_id FROM unit_access_roles LIMIT 0")?;
        connection.prepare_cached(
            "SELECT role_id,resource_key,action,data_scope FROM unit_access_grants LIMIT 0",
        )?;
        connection
            .prepare_cached("SELECT id,owner_unit_id,code FROM permission_schemes LIMIT 0")?;
        connection
            .prepare_cached("SELECT person_id,body FROM personnel_private_profiles LIMIT 0")?;
        connection.prepare_cached("SELECT id,kind,record_id,version,action,actor_id,occurred_at,body FROM audit_logs LIMIT 0")?;
        Ok(())
    }
}
impl Adapter for Sqlite {
    fn execute_statement(&self, statement: &str, values: &[String]) -> Result<u64> {
        let connection = self.connection.borrow();
        let affected = connection
            .prepare_cached(statement)?
            .execute(rusqlite::params_from_iter(values.iter()))?;
        Ok(affected as u64)
    }

    fn query_audits(&self, query: &AuditQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = audit_query::build(query, false);
        let count = if query.count_all {
            format!("(SELECT COUNT(*) FROM audit_logs r WHERE {})", sql.filter)
        } else {
            "0".into()
        };
        let statement = format!(
            "SELECT {count},(SELECT json_group_array(json(page.body)) FROM (SELECT {projection} AS body FROM audit_logs r WHERE {filter} ORDER BY {order} LIMIT {limit} OFFSET {offset}) page)",
            filter = sql.filter,
            order = sql.order,
            limit = query.limit.clamp(1, 200_000),
            offset = query.offset.max(0)
        );
        let (count, rows): (i64, String) = self.connection.borrow().query_cached(
            &statement,
            rusqlite::params_from_iter(sql.values.iter()),
            |row| Ok((row.get(0)?, row.get(1)?)),
        )?;
        Ok((count, serde_json::from_str(&rows)?))
    }
    fn attachment_category_counts(
        &self,
        company: &str,
    ) -> Result<std::collections::BTreeMap<i64, i64>> {
        let connection = self.connection.borrow();
        let mut statement = connection.prepare_cached("SELECT c.id,COUNT(*) FROM record_rows a JOIN record_rows c ON c.id=json_extract(a.body,'$.categoryId') AND c.kind='attachment-categories' WHERE a.kind='attachments' AND c.company=?1 GROUP BY c.id")?;
        statement
            .query_map([company], |row| Ok((row.get(0)?, row.get(1)?)))?
            .map(|row| row.map_err(Into::into))
            .collect()
    }
    fn import_preview_usage(&self, owner: i64) -> Result<ImportPreviewUsage> {
        let counts = self.connection.borrow().query_cached(
            &import_preview_query::usage_sql(false),
            [owner],
            |row| Ok([row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?]),
        )?;
        ImportPreviewUsage::from_counts(counts)
    }
    fn job_counts(&self) -> Result<Value> {
        let connection = self.connection.borrow();
        let mut statement=connection.prepare_cached("SELECT json_extract(body,'$.status'),COUNT(*) FROM record_rows WHERE kind='background-jobs' GROUP BY json_extract(body,'$.status')")?;
        let rows = statement.query_map([], |row| {
            Ok((row.get::<_, Option<String>>(0)?, row.get::<_, i64>(1)?))
        })?;
        let mut counts = serde_json::Map::new();
        for row in rows {
            let (status, count) = row?;
            counts.insert(status.unwrap_or_else(|| "Unknown".into()), json!(count));
        }
        Ok(Value::Object(counts))
    }
    fn job_output_bytes(&self) -> Result<u64> {
        let size: i64 = self.connection.borrow().query_cached(
            "SELECT COALESCE(SUM(length(content)),0) FROM files WHERE kind='job-output'",
            [],
            |row| row.get(0),
        )?;
        u64::try_from(size).map_err(|_| Error::unavailable("任务容量无效。"))
    }
    fn job_input_bytes(&self) -> Result<u64> {
        let size: i64 = self.connection.borrow().query_cached(
            "SELECT COALESCE(SUM(length(content)),0) FROM files WHERE kind='job-input'",
            [],
            |row| row.get(0),
        )?;
        u64::try_from(size).map_err(|_| Error::unavailable("任务输入容量无效。"))
    }
    fn query_communications(&self, q: &CommunicationQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let sql = communication::sql(q, false);
        self.query_page(&sql, "r.body", q.offset, q.limit.clamp(1, 100))
    }
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
    fn begin_read(&self) -> Result<()> {
        self.connection.borrow().execute_batch("BEGIN;")?;
        Ok(())
    }
    fn commit(&self) -> Result<()> {
        self.connection.borrow().execute_batch("COMMIT;")?;
        Ok(())
    }
    fn rollback(&self) -> Result<()> {
        let connection = self.connection.borrow();
        // An interrupted SQLite write may already have rolled back the whole transaction.
        if !connection.is_autocommit() {
            connection.execute_batch("ROLLBACK;")?;
        }
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
            .query_cached(
                "SELECT body FROM record_rows WHERE kind=?1 AND id=?2",
                params![kind, id],
                |r| r.get(0),
            )
            .optional()?;
        body.map(|s| serde_json::from_str(&s).map_err(Into::into))
            .transpose()
    }
    fn all(&self, kind: &str) -> Result<Vec<Value>> {
        let c = self.connection.borrow();
        let mut s =
            c.prepare_cached("SELECT body FROM record_rows WHERE kind=?1 ORDER BY id DESC")?;
        s.query_map([kind], |r| r.get::<_, String>(0))?
            .map(|r| serde_json::from_str(&r?).map_err(Into::into))
            .collect()
    }
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>> {
        let body: Option<String> = self
            .connection
            .borrow()
            .query_cached(
                "SELECT body FROM record_rows WHERE kind=?1 AND identity=?2",
                params![kind, identity],
                |row| row.get(0),
            )
            .optional()?;
        body.map(|body| serde_json::from_str(&body).map_err(Into::into))
            .transpose()
    }
    fn query_records(&self, q: &RecordQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let sql = super::record_query::sql(q, false);
        self.query_page(
            &sql,
            &super::sql::projection(q.fields, false)?,
            q.offset,
            q.limit.clamp(1, 100),
        )
    }
    fn query_workflow_summary(&self, q: &WorkflowSummaryQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, values) = super::workflow::summary(q, false);
        let (count, rows): (i64, String) = self.connection.borrow().query_cached(
            &sql,
            rusqlite::params_from_iter(values.iter()),
            |r| Ok((r.get(0)?, r.get(1)?)),
        )?;
        Ok((count, serde_json::from_str(&rows)?))
    }
    fn query_workflow_tasks(&self, q: &WorkflowTaskQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, values) = super::workflow::page(q, false);
        let (count, rows): (i64, String) = self.connection.borrow().query_cached(
            &sql,
            rusqlite::params_from_iter(values.iter()),
            |r| Ok((r.get(0)?, r.get(1)?)),
        )?;
        Ok((count, serde_json::from_str(&rows)?))
    }
    fn query_slice(
        &self,
        sql: &QuerySql,
        projection: &str,
        offset: i64,
        limit: i64,
    ) -> Result<Vec<Value>> {
        let connection = self.connection.borrow();
        let query = format!(
            "SELECT {projection} FROM record_rows r WHERE {} ORDER BY {} LIMIT {} OFFSET {}",
            sql.filter,
            sql.order,
            limit.clamp(1, 2000),
            offset.max(0)
        );
        let mut statement = connection.prepare_cached(&query)?;
        statement
            .query_map(rusqlite::params_from_iter(sql.values.iter()), |row| {
                row.get::<_, String>(0)
            })?
            .map(|row| Ok(serde_json::from_str(&row?)?))
            .collect()
    }
    fn query_json(&self, statement: &str, values: &[String]) -> Result<Value> {
        let body: String = self.connection.borrow().query_cached(
            statement,
            rusqlite::params_from_iter(values.iter()),
            |row| row.get(0),
        )?;
        Ok(serde_json::from_str(&body)?)
    }
    fn query_page(
        &self,
        sql: &QuerySql,
        projection: &str,
        offset: i64,
        limit: i64,
    ) -> Result<(i64, Vec<Value>)> {
        let connection = self.connection.borrow();
        let params = || rusqlite::params_from_iter(sql.values.iter());
        let query = format!("SELECT (SELECT COUNT(*) FROM record_rows r WHERE {filter}),
            (SELECT json_group_array(json(page.body)) FROM
             (SELECT {projection} AS body FROM record_rows r WHERE {filter} ORDER BY {order} LIMIT {limit} OFFSET {offset}) page)",
            filter=sql.filter, order=sql.order, limit=limit.clamp(1,200), offset=offset.max(0));
        let (count, rows): (i64, String) =
            connection.query_cached(&query, params(), |row| Ok((row.get(0)?, row.get(1)?)))?;
        Ok((count, serde_json::from_str(&rows)?))
    }
    fn insert(&self, r: &RecordWrite<'_>) -> Result<i64> {
        crate::identity_runtime::atomic(self, r.kind, || {
            let stored = crate::identity_runtime::document(
                r.kind,
                &crate::workflow_runtime::document(r.kind, r.body),
            );
            let b = &stored;
            let id = {
                let c = self.connection.borrow();
                c.execute("INSERT INTO records(kind,identity,version,owner_id,company,department,search_text,body) VALUES(?1,?2,?3,?4,?5,?6,'',?7)",params![r.kind,r.identity,b["versionNumber"].as_i64(),b["ownerUserId"].as_i64(),b["companyScope"].as_str().unwrap_or(""),b["departmentId"].as_str().unwrap_or(""),serde_json::to_string(b)?])?;
                c.last_insert_rowid()
            };
            crate::identity_runtime::persist(self, id, r.kind, r.body)?;
            Ok(id)
        })
    }
    fn update(&self, id: i64, expected: i64, r: &RecordWrite<'_>) -> Result<bool> {
        crate::identity_runtime::atomic(self, r.kind, || {
            let stored = crate::identity_runtime::document(
                r.kind,
                &crate::workflow_runtime::document(r.kind, r.body),
            );
            let b = &stored;
            let changed=self.connection.borrow().execute("UPDATE records SET identity=?1,version=?2,body=?3,owner_id=?4,company=?5,department=?6 WHERE kind=?7 AND id=?8 AND version=?9",params![r.identity,b["versionNumber"].as_i64(),serde_json::to_string(b)?,b["ownerUserId"].as_i64(),b["companyScope"].as_str().unwrap_or(""),b["departmentId"].as_str().unwrap_or(""),r.kind,id,expected])?==1;
            if changed {
                crate::identity_runtime::persist(self, id, r.kind, r.body)?;
            }
            Ok(changed)
        })
    }
    fn set_body(&self, id: i64, body: &Value) -> Result<()> {
        let kind: String = self.connection.borrow().query_cached(
            "SELECT kind FROM record_rows WHERE id=?1",
            [id],
            |row| row.get(0),
        )?;
        let stored = crate::identity_runtime::document(
            &kind,
            &crate::workflow_runtime::document(&kind, body),
        );
        let body = &stored;
        if self.connection.borrow().execute(
            "UPDATE records SET body=?1 WHERE id=?2",
            params![serde_json::to_string(body)?, id],
        )? != 1
        {
            return Err(Error::unavailable("写入记录编号失败。"));
        }
        if crate::identity_runtime::supports(&kind) {
            crate::identity_runtime::project(self, id)?;
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
        let mut statement = connection.prepare_cached("SELECT id,kind,record_id,version,action,actor_id,occurred_at,body FROM audit_logs WHERE id<?1 ORDER BY id DESC LIMIT ?2")?;
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
        let mut s=c.prepare_cached("SELECT id,kind,record_id,version,action,actor_id,occurred_at,note FROM history WHERE (?1 IS NULL OR kind=?1) AND (?2 IS NULL OR record_id=?2) ORDER BY id DESC LIMIT 10000")?;
        s.query_map(params![kind,id],|r|Ok(json!({"id":r.get::<_,i64>(0)?,"module":r.get::<_,String>(1)?,"recordId":r.get::<_,i64>(2)?,"versionNumber":r.get::<_,i64>(3)?,"action":r.get::<_,String>(4)?,"actorUserId":r.get::<_,i64>(5)?,"createdAt":r.get::<_,String>(6)?,"timestamp":r.get::<_,String>(6)?,"note":r.get::<_,String>(7)?,"description":r.get::<_,String>(7)?})))?.map(|r|r.map_err(Into::into)).collect()
    }
    fn has_history_except(&self, kind: &str, id: i64, allowed: &[&str]) -> Result<bool> {
        let placeholders = vec!["?"; allowed.len()].join(",");
        let filter = if allowed.is_empty() {
            String::new()
        } else {
            format!(" AND action NOT IN ({placeholders})")
        };
        let mut values = vec![rusqlite::types::Value::Text(kind.into()), id.into()];
        values.extend(
            allowed
                .iter()
                .map(|action| rusqlite::types::Value::Text((*action).into())),
        );
        Ok(self.connection.borrow().query_cached(
            &format!("SELECT EXISTS(SELECT 1 FROM history WHERE kind=? AND record_id=?{filter})"),
            rusqlite::params_from_iter(values),
            |row| row.get(0),
        )?)
    }
    fn credential(&self, id: i64) -> Result<Option<Credential>> {
        Ok(self
            .connection
            .borrow()
            .query_cached(
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
            .query_cached("SELECT body FROM settings WHERE name=?1", [name], |r| {
                r.get(0)
            })
            .optional()?;
        value
            .map(|v| serde_json::from_str(&v).map_err(Into::into))
            .transpose()
    }
    fn personnel_private_profile(&self, id: i64) -> Result<Option<Value>> {
        let value: Option<String> = self
            .connection
            .borrow()
            .query_cached(
                "SELECT body FROM personnel_private_profiles WHERE person_id=?1",
                [id],
                |r| r.get(0),
            )
            .optional()?;
        value
            .map(|body| serde_json::from_str(&body).map_err(Into::into))
            .transpose()
    }
    fn set_personnel_private_profile(&self, id: i64, body: &Value) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO personnel_private_profiles(person_id,body) VALUES(?1,?2) ON CONFLICT(person_id) DO UPDATE SET body=excluded.body",params![id,serde_json::to_string(body)?])?;
        Ok(())
    }
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO settings(name,version,body) VALUES(?1,?2,?3) ON CONFLICT(name) DO UPDATE SET version=excluded.version,body=excluded.body",params![name,version,serde_json::to_string(body)?])?;
        Ok(())
    }
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64> {
        Ok(self.connection.borrow().query_cached("SELECT COALESCE(SUM(length(f.content)),0) FROM files f JOIN record_rows r ON r.id=f.record_id WHERE r.kind='attachments' AND json_extract(r.body,'$.invoiceId')=?1",[invoice_id],|r|r.get(0))?)
    }
    fn insert_blob(&self, b: &BlobWrite<'_>) -> Result<()> {
        self.connection.borrow().execute("INSERT INTO files(kind,record_id,file_name,media_type,digest,content,created_at) VALUES(?1,?2,?3,?4,?5,?6,?7)",params![b.kind,b.record_id,b.file_name,b.media_type,b.digest,b.content,b.created_at])?;
        Ok(())
    }
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>> {
        Ok(self.connection.borrow().query_cached("SELECT file_name,media_type,digest,content FROM files WHERE record_id=?1 AND kind=?2 ORDER BY id DESC LIMIT 1",params![record_id,kind],|r|Ok(Blob{file_name:r.get(0)?,media_type:r.get(1)?,digest:r.get(2)?,content:r.get(3)?})).optional()?)
    }
    fn blob_info(&self, record_id: i64, kind: &str) -> Result<Option<BlobInfo>> {
        let info: Option<(String, String, String, i64)> = self.connection.borrow().query_cached("SELECT file_name,media_type,digest,length(content) FROM files WHERE record_id=?1 AND kind=?2 ORDER BY id DESC LIMIT 1",params![record_id,kind],|row|Ok((row.get(0)?,row.get(1)?,row.get(2)?,row.get(3)?))).optional()?;
        info.map(|(file_name, media_type, digest, size)| {
            Ok(BlobInfo {
                file_name,
                media_type,
                digest,
                size: u64::try_from(size).map_err(|_| Error::unavailable("文件大小无效。"))?,
            })
        })
        .transpose()
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
        if Self::version(&source)? != SCHEMA_VERSION {
            return Err(Error::unavailable(
                "请先在受管暂存目录升级备份，再替换当前业务库。",
            ));
        }
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
        operation::check()?;
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
