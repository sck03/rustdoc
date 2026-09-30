//! Version 5 is the first durable Rust baseline. Never rewrite an applied step.
use crate::{Error, Result};

pub const MIN_SUPPORTED_SCHEMA_VERSION: i64 = 5;
pub const SCHEMA_VERSION: i64 = MIN_SUPPORTED_SCHEMA_VERSION + MIGRATIONS.len() as i64;

pub(crate) struct Migration {
    pub sqlite: &'static str,
    #[cfg(feature = "postgres")]
    pub postgres: &'static str,
}

const MIGRATIONS: &[Migration] = &[
    Migration {
        // v5 -> v6: permit later version markers without replacing business tables.
        sqlite:
            "CREATE TABLE schema_version_upgrade (version INTEGER PRIMARY KEY CHECK(version >= 5));
        INSERT INTO schema_version_upgrade SELECT version FROM schema_version;
        DROP TABLE schema_version;
        ALTER TABLE schema_version_upgrade RENAME TO schema_version;",
        #[cfg(feature = "postgres")]
        postgres:
            "ALTER TABLE schema_version DROP CONSTRAINT schema_version_version_check;
        ALTER TABLE schema_version ADD CONSTRAINT schema_version_version_check CHECK(version >= 5);",
    },
    Migration {
        sqlite: include_str!("migrations/007-sqlite.sql"),
        #[cfg(feature = "postgres")]
        postgres: include_str!("migrations/007-postgres.sql"),
    },
];

pub(crate) fn pending(version: i64) -> Result<&'static [Migration]> {
    if !(MIN_SUPPORTED_SCHEMA_VERSION..=SCHEMA_VERSION).contains(&version) {
        return Err(Error::unavailable(format!(
            "数据库版本 {version} 不受当前程序支持（可升级范围 {MIN_SUPPORTED_SCHEMA_VERSION}—{SCHEMA_VERSION}）。已保留原库，未重新初始化。"
        )));
    }
    Ok(&MIGRATIONS[(version - MIN_SUPPORTED_SCHEMA_VERSION) as usize..])
}
