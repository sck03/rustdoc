//! Keep relational projections atomic even for single storage writes outside
//! an application transaction. Each projection owns its own data extraction.
use crate::{Adapter, Error, Result};
pub(crate) fn atomic<T>(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    work: impl FnOnce() -> Result<T>,
) -> Result<T> {
    if !crate::resource_index::supports(kind) {
        return crate::identity_runtime::atomic(db, kind, work);
    }
    db.execute_statement("SAVEPOINT record_projection", &[])?;
    match work() {
        Ok(value) => {
            db.execute_statement("RELEASE SAVEPOINT record_projection", &[])?;
            Ok(value)
        }
        Err(cause) => {
            crate::operation::without_control(|| {
                db.execute_statement("ROLLBACK TO SAVEPOINT record_projection", &[])?;
                db.execute_statement("RELEASE SAVEPOINT record_projection", &[])?;
                Ok::<_, Error>(())
            })?;
            Err(cause)
        }
    }
}
pub(crate) fn persist(
    db: &(impl Adapter + ?Sized),
    id: i64,
    kind: &str,
    body: &serde_json::Value,
) -> Result<()> {
    crate::identity_runtime::persist(db, id, kind, body)?;
    crate::resource_index::persist(db, id, kind, body)
}
