//! Permanent file metadata. Physical bytes and network I/O belong to the file store.
use crate::{Adapter, Error, Result, sql};
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FileObject {
    pub digest: String,
    pub size: u64,
}
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FileInfo {
    pub file_name: String,
    pub media_type: String,
    pub object: FileObject,
}
pub struct FileWrite<'a> {
    pub kind: &'a str,
    pub record_id: i64,
    pub file_name: &'a str,
    pub media_type: &'a str,
    pub object: &'a FileObject,
    pub created_at: &'a str,
}
pub(crate) fn insert(db: &(impl Adapter + ?Sized), file: &FileWrite<'_>) -> Result<()> {
    let object = file.object;
    if object.size == 0
        || object.size > i64::MAX as u64
        || object.digest.len() != 64
        || !object
            .digest
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(Error::unavailable("文件对象元数据无效。"));
    }
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let digest = sql::bind(&mut values, &object.digest, pg);
    let size = sql::bind(&mut values, object.size, pg);
    let created = sql::bind(&mut values, file.created_at, pg);
    db.execute_statement(&format!("INSERT INTO file_objects(digest,size_bytes,created_at) VALUES({digest},CAST({size} AS BIGINT),{created}) ON CONFLICT(digest) DO NOTHING"), &values)?;
    let actual = db.query_json(
        &format!("SELECT CAST(size_bytes AS TEXT) FROM file_objects WHERE digest={digest}"),
        &values[..1],
    )?;
    if actual.as_u64() != Some(object.size) {
        return Err(Error::unavailable("相同摘要的文件大小不一致。"));
    }
    let mut values = vec![];
    let mut bind = |value: &str| sql::bind(&mut values, value, pg);
    let kind = bind(file.kind);
    let record = bind(&file.record_id.to_string());
    let name = bind(file.file_name);
    let media = bind(file.media_type);
    let digest = bind(&object.digest);
    let created = bind(file.created_at);
    db.execute_statement(&format!("INSERT INTO file_versions(record_id,kind,file_name,media_type,digest,created_at) VALUES(CAST({record} AS BIGINT),{kind},{name},{media},{digest},{created})"), &values)?;
    Ok(())
}
pub(crate) fn get(
    db: &(impl Adapter + ?Sized),
    record: i64,
    kind: &str,
) -> Result<Option<FileInfo>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let kind = sql::bind(&mut values, kind, pg);
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let projection = format!(
        "{json}('fileName',v.file_name,'mediaType',v.media_type,'object',{json}('digest',o.digest,'size',o.size_bytes))"
    );
    let select = format!(
        "SELECT {projection} AS body FROM file_versions v JOIN file_objects o ON o.digest=v.digest WHERE v.record_id=CAST({record} AS BIGINT) AND v.kind={kind}"
    );
    let rows = rows(db, &select, &values)?;
    rows.into_iter()
        .next()
        .map(|value| serde_json::from_value(value).map_err(Into::into))
        .transpose()
}
pub(crate) fn remove(db: &(impl Adapter + ?Sized), record: i64, kind: Option<&str>) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let mut query = format!("DELETE FROM file_versions WHERE record_id=CAST({record} AS BIGINT)");
    if let Some(kind) = kind {
        query += &format!(" AND kind={}", sql::bind(&mut values, kind, pg));
    }
    db.execute_statement(&query, &values)?;
    Ok(())
}
pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    record: i64,
    prefix: &str,
    after: &str,
    limit: i64,
) -> Result<(i64, Vec<FileInfo>)> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let prefix_arg = sql::bind(&mut values, prefix, pg);
    let condition = format!(
        "v.record_id=CAST({record} AS BIGINT) AND substr(v.kind,1,{})={prefix_arg}",
        prefix.len()
    );
    let total = db
        .query_json(
            &format!("SELECT CAST(COUNT(*) AS TEXT) FROM file_versions v WHERE {condition}"),
            &values,
        )?
        .as_i64()
        .ok_or_else(|| Error::unavailable("文件数量无效。"))?;
    let after = sql::bind(&mut values, format!("{prefix}{after}"), pg);
    let limit = sql::bind(&mut values, limit.clamp(1, 201), pg);
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let select = format!(
        "SELECT {json}('fileName',v.file_name,'mediaType',v.media_type,'object',{json}('digest',o.digest,'size',o.size_bytes)) AS body FROM file_versions v JOIN file_objects o ON o.digest=v.digest WHERE {condition} AND v.kind>{after} ORDER BY v.kind LIMIT CAST({limit} AS BIGINT)"
    );
    Ok((
        total,
        rows(db, &select, &values)?
            .into_iter()
            .map(serde_json::from_value)
            .collect::<std::result::Result<_, _>>()?,
    ))
}
pub(crate) fn objects(
    db: &(impl Adapter + ?Sized),
    after: &str,
    limit: i64,
    unused: bool,
) -> Result<Vec<FileObject>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let after = sql::bind(&mut values, after, pg);
    let limit = sql::bind(&mut values, limit.clamp(1, 200), pg);
    let json = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let unused = if unused {
        " AND NOT EXISTS(SELECT 1 FROM file_versions v WHERE v.digest=o.digest)"
    } else {
        ""
    };
    let query = format!(
        "SELECT {json}('digest',o.digest,'size',o.size_bytes) AS body FROM file_objects o WHERE o.digest>{after}{unused} ORDER BY o.digest LIMIT CAST({limit} AS BIGINT)"
    );
    rows(db, &query, &values)?
        .into_iter()
        .map(|value| serde_json::from_value(value).map_err(Into::into))
        .collect()
}

pub(crate) fn pending_copies(
    db: &(impl Adapter + ?Sized),
    identity: &str,
) -> Result<Vec<FileObject>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let identity = sql::bind(&mut values, identity, pg);
    let constructor = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    rows(db, &format!("SELECT {constructor}('digest',o.digest,'size',o.size_bytes) AS body FROM file_migration_queue q JOIN file_objects o ON o.digest=q.digest WHERE q.storage_identity={identity} ORDER BY q.digest LIMIT 200"), &values)?
        .into_iter().map(|value| serde_json::from_value(value).map_err(Into::into)).collect()
}
pub(crate) fn locate(db: &(impl Adapter + ?Sized), identity: &str, digest: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let identity = sql::bind(&mut values, identity, pg);
    let digest = sql::bind(&mut values, digest, pg);
    db.execute_statement(&format!("INSERT INTO file_locations(storage_identity,digest) VALUES({identity},{digest}) ON CONFLICT DO NOTHING"), &values)?;
    db.execute_statement(
        &format!(
            "DELETE FROM file_migration_queue WHERE storage_identity={identity} AND digest={digest}"
        ),
        &values,
    )?;
    Ok(())
}
pub(crate) fn begin_migration(db: &(impl Adapter + ?Sized), identity: &str) -> Result<()> {
    let mut values = vec![];
    let identity = sql::bind(&mut values, identity, db.provider() == "PostgreSQL");
    db.execute_statement(
        &format!("INSERT INTO file_migrations VALUES({identity}) ON CONFLICT DO NOTHING"),
        &values,
    )?;
    // A resumed run validates existing targets again, outside the application gate.
    db.execute_statement(&format!("INSERT INTO file_migration_queue SELECT {identity},digest FROM file_objects WHERE true ON CONFLICT DO NOTHING"), &values)?;
    Ok(())
}
pub(crate) fn end_migration(db: &(impl Adapter + ?Sized), identity: &str) -> Result<()> {
    let mut values = vec![];
    let identity = sql::bind(&mut values, identity, db.provider() == "PostgreSQL");
    db.execute_statement(
        &format!("DELETE FROM file_migrations WHERE storage_identity={identity}"),
        &values,
    )?;
    Ok(())
}
pub(crate) fn referenced(db: &(impl Adapter + ?Sized), digest: &str) -> Result<bool> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let digest = sql::bind(&mut values, digest, pg);
    let value = db.query_json(
        &format!("SELECT CAST(COUNT(*) AS TEXT) FROM file_versions WHERE digest={digest}"),
        &values,
    )?;
    value
        .as_u64()
        .map(|v| v > 0)
        .ok_or_else(|| Error::unavailable("文件引用统计无效。"))
}
pub(crate) fn remove_unused(db: &(impl Adapter + ?Sized), digest: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let digest = sql::bind(&mut values, digest, pg);
    db.execute_statement(&format!("DELETE FROM file_objects WHERE digest={digest} AND NOT EXISTS(SELECT 1 FROM file_versions v WHERE v.digest=file_objects.digest)"), &values)?;
    Ok(())
}
fn rows(
    db: &(impl Adapter + ?Sized),
    select: &str,
    values: &[String],
) -> Result<Vec<serde_json::Value>> {
    let query = if db.provider() == "PostgreSQL" {
        format!("SELECT COALESCE(jsonb_agg(body),'[]'::jsonb)::text FROM ({select}) selected")
    } else {
        format!("SELECT json_group_array(json(body)) FROM ({select}) selected")
    };
    serde_json::from_value(db.query_json(&query, values)?).map_err(Into::into)
}

pub(crate) fn statistics(db: &(impl Adapter + ?Sized)) -> Result<serde_json::Value> {
    let constructor = if db.provider() == "PostgreSQL" {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    db.query_json(&format!("SELECT CAST({constructor}('fileCount',file_count,'physicalBytes',physical_bytes,'unreferencedBytes',physical_bytes-referenced_bytes,'logicalBytes',logical_bytes) AS TEXT) FROM file_storage_totals WHERE id=1"), &[])
}

pub(crate) fn owner_company(db: &(impl Adapter + ?Sized), record: i64) -> Result<String> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let record = sql::bind(&mut values, record, pg);
    let scope = sql::field("companyScope", pg);
    let constructor = if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let value = db.query_json(&format!("SELECT CAST({constructor}('company',COALESCE({scope},'')) AS TEXT) FROM records r WHERE r.id=CAST({record} AS BIGINT)"), &values)?;
    value["company"]
        .as_str()
        .map(str::to_owned)
        .ok_or_else(|| Error::unavailable("文件所属组织无效。"))
}

pub(crate) fn usage(db: &(impl Adapter + ?Sized), company: &str) -> Result<u64> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    db.query_json(&format!("SELECT CAST(COALESCE((SELECT logical_bytes FROM file_usage_totals WHERE company={company}),0) AS TEXT)"), &values)?
        .as_u64().ok_or_else(|| Error::unavailable("文件容量统计无效。"))
}

pub(crate) fn verify_accounting(db: &(impl Adapter + ?Sized)) -> Result<()> {
    let scope = sql::field("companyScope", db.provider() == "PostgreSQL");
    let inconsistent = db.query_json(&format!("SELECT CAST(COUNT(*) AS TEXT) FROM (
        SELECT 1 AS mismatch FROM file_objects o WHERE reference_count<>(SELECT COUNT(*) FROM file_versions v WHERE v.digest=o.digest)
        UNION ALL SELECT 1 FROM file_storage_totals WHERE id=1 AND (
            file_count<>(SELECT COUNT(*) FROM file_objects) OR physical_bytes<>(SELECT COALESCE(SUM(size_bytes),0) FROM file_objects) OR
            referenced_bytes<>(SELECT COALESCE(SUM(size_bytes),0) FROM file_objects o WHERE EXISTS(SELECT 1 FROM file_versions v WHERE v.digest=o.digest)) OR
            logical_bytes<>(SELECT COALESCE(SUM(o.size_bytes),0) FROM file_versions v JOIN file_objects o ON o.digest=v.digest))
        UNION ALL SELECT 1 FROM (SELECT company,SUM(amount) AS difference FROM (
            SELECT COALESCE({scope},'') AS company,o.size_bytes AS amount FROM file_versions v JOIN file_objects o ON o.digest=v.digest JOIN records r ON r.id=v.record_id
            UNION ALL SELECT company,-logical_bytes AS amount FROM file_usage_totals
        ) contributions GROUP BY company HAVING SUM(amount)<>0) groups
    ) mismatches"), &[])?;
    if inconsistent.as_u64() != Some(0) {
        return Err(Error::unavailable("文件容量账目不一致，请核对数据库。"));
    }
    Ok(())
}
