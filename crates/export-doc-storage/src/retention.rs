//! Archived references share the original immutable FileStore objects.
use crate::{Adapter, Error, ErrorKind, Result, sql};
use serde_json::{Value, json};
fn conflict() -> Error {
    Error::new(
        ErrorKind::Conflict,
        "档案保留记录已变化，请刷新后重新预检。",
    )
}
fn select(db: &(impl Adapter + ?Sized), condition: &str) -> String {
    let json = if db.provider() == "PostgreSQL" {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    format!(
        "SELECT {json}('id',f.id,'recordId',f.record_id,'fileKind',f.file_kind,'archiveClass',f.archive_class,'fileName',f.file_name,'company',f.company,'digest',f.digest,'basisDate',f.basis_date,'revision',f.revision,'size',o.size_bytes,'archiveIdentity',COALESCE((SELECT storage_identity FROM file_locations l WHERE l.digest=o.digest AND l.archived=1),''),'days',COALESCE(p.days,0),'policyRevision',COALESCE(p.revision,0),'held',COALESCE(h.active,0),'holdReason',COALESCE(h.reason,''),'holdRevision',COALESCE(h.revision,0),'activeReferences',(SELECT COUNT(*) FROM file_versions v WHERE v.digest=f.digest)) AS body FROM file_retention_refs f JOIN file_objects o ON o.digest=f.digest LEFT JOIN retention_policies p ON p.archive_class=f.archive_class LEFT JOIN retention_holds h ON h.digest=f.digest {condition}"
    )
}
pub(crate) fn page(db: &(impl Adapter + ?Sized), after: &str, limit: i64) -> Result<Value> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let after = sql::bind(&mut args, after, pg);
    let limit = sql::bind(&mut args, limit.clamp(1, 101), pg);
    Ok(json!(crate::file_catalog::rows(
        db,
        &select(
            db,
            &format!("WHERE f.id>{after} ORDER BY f.id LIMIT CAST({limit} AS BIGINT)")
        ),
        &args
    )?))
}
pub(crate) fn get(db: &(impl Adapter + ?Sized), id: &str) -> Result<Option<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let key = sql::bind(&mut args, id, pg);
    Ok(
        crate::file_catalog::rows(db, &select(db, &format!("WHERE f.id={key}")), &args)?
            .into_iter()
            .next(),
    )
}
pub(crate) fn policy(
    db: &(impl Adapter + ?Sized),
    class: &str,
    days: i64,
    revision: i64,
) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let class = sql::bind(&mut args, class, pg);
    let days = sql::bind(&mut args, days, pg);
    let next = sql::bind(&mut args, revision + 1, pg);
    let changed=db.execute_statement(&format!("INSERT INTO retention_policies(archive_class,days,revision) SELECT {class},CAST({days} AS INTEGER),CAST({next} AS BIGINT) WHERE CAST({next} AS BIGINT)=1 OR EXISTS(SELECT 1 FROM retention_policies WHERE archive_class={class}) ON CONFLICT(archive_class) DO UPDATE SET days=excluded.days,revision=excluded.revision WHERE retention_policies.revision=excluded.revision-1"),&args)?;
    if changed != 1 {
        return Err(conflict());
    }
    Ok(())
}
pub(crate) fn classify(
    db: &(impl Adapter + ?Sized),
    id: &str,
    class: &str,
    basis: &str,
    revision: i64,
) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let id = sql::bind(&mut args, id, pg);
    let class = sql::bind(&mut args, class, pg);
    let basis = sql::bind(&mut args, basis, pg);
    let version = sql::bind(&mut args, revision, pg);
    if db.execute_statement(&format!("UPDATE file_retention_refs SET archive_class={class},basis_date={basis},revision=revision+1 WHERE id={id} AND revision=CAST({version} AS BIGINT)"),&args)?!=1{return Err(conflict());}
    Ok(())
}
pub(crate) fn hold(
    db: &(impl Adapter + ?Sized),
    digest: &str,
    active: bool,
    reason: &str,
    revision: i64,
) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let digest = sql::bind(&mut args, digest, pg);
    let active = sql::bind(&mut args, i32::from(active), pg);
    let reason = sql::bind(&mut args, reason, pg);
    let next = sql::bind(&mut args, revision + 1, pg);
    if db.execute_statement(&format!("INSERT INTO retention_holds(digest,active,reason,revision) SELECT {digest},CAST({active} AS INTEGER),{reason},CAST({next} AS BIGINT) WHERE EXISTS(SELECT 1 FROM file_objects WHERE digest={digest}) AND (CAST({next} AS BIGINT)=1 OR EXISTS(SELECT 1 FROM retention_holds WHERE digest={digest})) ON CONFLICT(digest) DO UPDATE SET active=excluded.active,reason=excluded.reason,revision=excluded.revision WHERE retention_holds.revision=excluded.revision-1"),&args)?!=1{return Err(conflict());}
    Ok(())
}
pub(crate) fn remove(db: &(impl Adapter + ?Sized), id: &str, revision: i64) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut args = vec![];
    let id = sql::bind(&mut args, id, pg);
    let version = sql::bind(&mut args, revision, pg);
    if db.execute_statement(&format!("DELETE FROM file_retention_refs WHERE id={id} AND revision=CAST({version} AS BIGINT) AND NOT EXISTS(SELECT 1 FROM file_versions v WHERE v.digest=file_retention_refs.digest) AND NOT EXISTS(SELECT 1 FROM retention_holds h WHERE h.digest=file_retention_refs.digest AND h.active=1)"),&args)?!=1{return Err(conflict());}
    Ok(())
}
