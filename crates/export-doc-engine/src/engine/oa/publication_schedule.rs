//! Activation is a cancellable CAS transition; immutable versions survive failure.
use super::*;
pub(super) fn tick(service: &NativeService, now: chrono::DateTime<chrono::Utc>) -> Result<()> {
    let entries = service
        .store
        .read(|tx| Ok(tx.workflow_activation_list("", Some(&now.to_rfc3339()))?))?;
    for entry in entries {
        crate::operation::check()?;
        let id = text(&entry, "id")
            .parse::<i64>()
            .map_err(|_| super::super::error::unavailable("发布调度编号损坏。"))?;
        let expected = entry["versionNumber"]
            .as_i64()
            .ok_or_else(|| super::super::error::unavailable("发布调度版本损坏。"))?;
        let result = service.store.transaction(|tx| {
            let current = tx
                .workflow_activation_list(&text(&entry, "companyCode"), Some(&now.to_rfc3339()))?
                .into_iter()
                .find(|e| e["id"] == entry["id"]);
            if current
                .as_ref()
                .is_none_or(|e| e["versionNumber"] != expected || e["state"] != "Pending")
            {
                return Ok(());
            }
            let actor = super::super::units::resolve(
                tx,
                records::positive(&entry, "actorId", "发布身份")?,
                service.store.edition,
                &text(&entry, "contextId"),
            )?;
            super::definitions::activate_scheduled(tx, &actor, &entry)?;
            if !tx.workflow_activation_state(id, expected, "Applied", "")? {
                return Err(conflict("发布调度已被其他操作处理。"));
            }
            Ok(())
        });
        if let Err(cause) = result {
            if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) {
                service.store.transaction(|tx| {
                    tx.workflow_activation_state(
                        id,
                        expected,
                        "Blocked",
                        &format!("Activation{}", cause.status.unwrap_or(409)),
                    )?;
                    Ok(())
                })?;
            } else {
                return Err(cause);
            }
        }
    }
    Ok(())
}
