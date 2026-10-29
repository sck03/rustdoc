//! Fresh pre-production baseline. Earlier experimental databases are never opened or rewritten.
use crate::{Error, Result};
pub const SCHEMA_VERSION: i64 = 10;
pub const MIN_SUPPORTED_SCHEMA_VERSION: i64 = SCHEMA_VERSION;
pub(crate) fn validate(version: i64) -> Result<()> {
    if version != SCHEMA_VERSION {
        return Err(Error::unavailable(format!(
            "数据库版本 {version} 不受支持；本程序使用全新版本 {SCHEMA_VERSION}。请备份旧测试数据并显式建立新数据目录，程序未删除或改写原库。"
        )));
    }
    Ok(())
}
