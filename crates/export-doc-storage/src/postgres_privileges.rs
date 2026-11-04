//! One privilege definition for online maintenance and reviewed offline restore plans.
use crate::{Error, Result};

pub fn role_name(value: &str) -> Result<&str> {
    if value.is_empty()
        || value.len() > 63
        || !value.as_bytes()[0].is_ascii_alphabetic() && value.as_bytes()[0] != b'_'
        || !value
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || c == b'_')
    {
        return Err(Error::unavailable("PostgreSQL 角色名称无效。"));
    }
    Ok(value)
}
pub fn grants(application: &str) -> Result<String> {
    let application = role_name(application)?;
    Ok(include_str!("postgres_privileges.sql")
        .replace("$APPLICATION", &format!("\"{application}\"")))
}
pub fn restore_plan(owners: &[String], application: &str) -> Result<String> {
    let mut sql = String::from(
        "-- Maintenance and NOLOGIN ownership remain separate from the business login.\n",
    );
    for owner in owners {
        let owner = role_name(owner)?;
        sql.push_str(&format!(
            "REASSIGN OWNED BY \"{owner}\" TO :\"owner_role\";\n"
        ));
    }
    sql.push_str("SET ROLE :\"owner_role\";\n");
    sql.push_str(&grants(application)?);
    Ok(sql)
}
