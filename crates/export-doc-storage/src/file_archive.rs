//! Verified archive routing belongs to the file catalog, independently of business references.
use crate::{Adapter, Result, sql};
pub(crate) fn activate(db: &(impl Adapter + ?Sized), identity: &str, digest: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let digest = sql::bind(&mut values, digest, pg);
    db.execute_statement(
        &format!("UPDATE file_locations SET archived=0 WHERE digest={digest}"),
        &values,
    )?;
    let identity = sql::bind(&mut values, identity, pg);
    db.execute_statement(&format!("INSERT INTO file_locations(storage_identity,digest,archived) VALUES({identity},{digest},1) ON CONFLICT(storage_identity,digest) DO UPDATE SET archived=1"),&values)?;
    Ok(())
}
