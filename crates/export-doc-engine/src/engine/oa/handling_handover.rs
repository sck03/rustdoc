//! Reassignment creates ordinary business events; it never rewrites request content.
use super::*;

pub(super) fn notify(
    tx: &Connection,
    actor: &Actor,
    previous: &Value,
    saved: &Value,
) -> Result<()> {
    let changed: Vec<_> = saved["handlingServices"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|service| {
            previous["handlingServices"]
                .as_array()
                .into_iter()
                .flatten()
                .any(|old| {
                    old["key"] == service["key"]
                        && old["handlerUserIds"] != service["handlerUserIds"]
                })
        })
        .map(|s| text(s, "key"))
        .collect();
    if changed.is_empty() {
        return Ok(());
    }
    for kind in ["oa-general", "supply-requests", "bookings"] {
        let mut offset = 0;
        loop {
            crate::operation::check()?;
            let (count, rows) = tx.query_records(&RecordQuery {
                kind,
                company: &actor.company,
                offset,
                limit: 100,
                ..Default::default()
            })?;
            for row in rows {
                if !handling::assigned(&changed, &row) || !handling::outstanding(&row) {
                    continue;
                }
                let note = format!("办理分工交接至：{}", handling::names(tx, actor, &row)?);
                if kind == "oa-general" {
                    let event = append_event(tx, actor, &row, "reassign", &note)?;
                    super::super::communication::on_event(
                        tx,
                        actor,
                        &row,
                        &event,
                        &json!({"resource":"office.general"}),
                    )?;
                } else {
                    super::super::office_events::append(
                        tx, actor, kind, &row, "Reassign", 0, &note,
                    )?;
                }
            }
            offset += 100;
            if offset >= count {
                break;
            }
        }
    }
    Ok(())
}
