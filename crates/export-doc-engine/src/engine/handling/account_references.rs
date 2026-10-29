use super::*;

/// Inspect the account after its write, using the resulting permission source.
/// Rejection rolls back an account, personnel sync, or shared scheme atomically.
pub(in crate::engine) fn validate_account_in(
    tx: &Connection,
    actor: &Actor,
    user: &Value,
    company: &str,
) -> Result<()> {
    let id = super::super::records::positive(user, "id", "账号")?;
    let assigned: Vec<_> = services(tx, &company)?
        .into_iter()
        .filter(|service| {
            service["handlerUserIds"]
                .as_array()
                .is_some_and(|ids| ids.contains(&json!(id)))
        })
        .collect();
    if assigned.is_empty() {
        return Ok(());
    }
    for service in assigned {
        crate::operation::check()?;
        let resource = category_resource(&text(&service, "category"))
            .map_err(|_| unavailable("办理分工类别损坏。"))?;
        let current = super::super::units::for_operation(
            tx,
            id,
            actor.edition,
            company,
            resource,
            action(resource)?,
        )
        .map_err(|e| {
            if matches!(e.status, Some(400 | 403 | 404)) {
                conflict("账号仍在办理分工中，请先交接办理人员再停用或调整权限。")
            } else {
                e
            }
        })?;
        if current.company != company
            || auth::authorize(&current, resource, "view").is_err()
            || auth::authorize(&current, resource, action(resource)?).is_err()
        {
            return Err(conflict(
                "权限调整会使办理分工失效，请先在办理分工中交接人员。",
            ));
        }
    }
    Ok(())
}
