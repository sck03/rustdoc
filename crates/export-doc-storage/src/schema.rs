//! Fresh pre-production baseline. Earlier experimental databases are never opened or rewritten.
use crate::{Error, Result};
use sha2::{Digest, Sha256};
pub const SCHEMA_VERSION: i64 = 11;
pub const MIN_SUPPORTED_SCHEMA_VERSION: i64 = SCHEMA_VERSION;
pub(crate) fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|byte| format!("{byte:02x}")).collect()
}
pub fn model_fingerprint(postgres: bool) -> String {
    let sources = if postgres {
        [
            include_str!("postgres.sql"),
            include_str!("identity_runtime_postgres.sql"),
            include_str!("personnel_foundation_postgres.sql"),
            include_str!("permission_versions_postgres.sql"),
            include_str!("access_scope_postgres.sql"),
            include_str!("workflow_runtime_postgres.sql"),
        ]
    } else {
        [
            include_str!("sqlite.sql"),
            include_str!("identity_runtime_sqlite.sql"),
            include_str!("personnel_foundation_sqlite.sql"),
            include_str!("permission_versions_sqlite.sql"),
            include_str!("access_scope_sqlite.sql"),
            include_str!("workflow_runtime_sqlite.sql"),
        ]
    };
    let mut digest = Sha256::new();
    for source in sources {
        digest.update(source.replace("\r\n", "\n").as_bytes());
        digest.update([0]);
    }
    hex(digest.finalize().as_ref())
}
pub(crate) fn validate_model(
    postgres: bool,
    model: &str,
    recorded_shape: &str,
    shape: &str,
) -> Result<()> {
    if model != model_fingerprint(postgres) || recorded_shape != shape {
        return Err(Error::unavailable(
            "数据库模型指纹或结构不匹配，已保留原库；请显式建立新的数据目录或数据库。",
        ));
    }
    Ok(())
}
pub(crate) fn sqlite_shape(connection: &rusqlite::Connection) -> Result<String> {
    let mut statement = connection.prepare("SELECT type,name,tbl_name,COALESCE(sql,'') FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name")?;
    let mut digest = Sha256::new();
    for row in statement.query_map([], |r| {
        Ok([r.get::<_, String>(0)?, r.get(1)?, r.get(2)?, r.get(3)?])
    })? {
        digest.update(serde_json::to_vec(&row?)?);
    }
    Ok(hex(digest.finalize().as_ref()))
}
#[cfg(feature = "postgres")]
pub(crate) fn postgres_shape(client: &mut impl ::postgres::GenericClient) -> Result<String> {
    let mut digest = Sha256::new();
    for query in [
        "SELECT table_name,column_name,ordinal_position::text,data_type,udt_name,is_nullable,COALESCE(column_default,'') FROM information_schema.columns WHERE table_schema='public' ORDER BY table_name,ordinal_position",
        "SELECT c.relname,k.conname,pg_get_constraintdef(k.oid) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname,k.conname",
        "SELECT tablename,indexname,indexdef FROM pg_indexes WHERE schemaname='public' ORDER BY tablename,indexname",
        "SELECT c.relname,t.tgname,pg_get_triggerdef(t.oid) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal ORDER BY c.relname,t.tgname",
        "SELECT viewname,definition FROM pg_views WHERE schemaname='public' ORDER BY viewname",
        "SELECT p.proname,pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' ORDER BY p.proname,pg_get_function_identity_arguments(p.oid)",
    ] {
        for row in client.query(query, &[])? {
            let values = (0..row.len())
                .map(|i| row.try_get::<_, String>(i))
                .collect::<std::result::Result<Vec<_>, _>>()?;
            digest.update(serde_json::to_vec(&values)?);
        }
    }
    Ok(hex(digest.finalize().as_ref()))
}
pub(crate) fn validate(version: i64) -> Result<()> {
    if version != SCHEMA_VERSION {
        return Err(Error::unavailable(format!(
            "数据库版本 {version} 不受支持；本程序使用全新版本 {SCHEMA_VERSION}。请备份旧测试数据并显式建立新数据目录，程序未删除或改写原库。"
        )));
    }
    Ok(())
}
