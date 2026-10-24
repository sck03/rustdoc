//! Authorized, projected reads for cross-source worklists.
use super::super::{auth, error::Result, store::Actor};
use export_doc_storage::Connection;
use serde_json::Value;

pub(super) fn rows(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    resource: &str,
    actions: &[&str],
    office: bool,
) -> Result<Vec<Value>> {
    let mut result = vec![];
    let scoped = !matches!(resource, "office.rooms" | "office.supplies");
    let scopes: Vec<_> = actions
        .iter()
        .map(|action| auth::data_scope(actor, resource, action))
        .collect();
    let names = match kind {
        "invoices" => &["invoiceNo", "type", "customerNameEN"][..],
        "crm-follow-ups" => &[
            "crmCustomerId",
            "summary",
            "nextAction",
            "nextFollowUpAt",
            "isCompleted",
        ][..],
        "people" => &[
            "employeeNumber",
            "fullName",
            "probationEndsOn",
            "contractEndsOn",
            "organization",
        ][..],
        "bookings" => &[
            "title",
            "applicantName",
            "startsAt",
            "endsAt",
            "handlingKey",
        ][..],
        _ => &[
            "applicantName",
            "purpose",
            "supplyName",
            "quantity",
            "returnedQuantity",
            "isReturnable",
            "returnDueDate",
            "handlingKey",
        ][..],
    };
    let fields: Vec<_> = [
        "id",
        "status",
        "companyScope",
        "departmentId",
        "ownerUserId",
    ]
    .into_iter()
    .chain(names.iter().copied())
    .map(|field| {
        (
            field,
            if field == "fullName" {
                "profile.fullName"
            } else {
                field
            },
        )
    })
    .collect();
    let mut offset = 0;
    loop {
        let (total, batch) = tx.query_scoped(&export_doc_storage::ScopedQuery {
            kind,
            scopes: if scoped { &scopes } else { &[] },
            company: office.then_some(actor.company.as_str()),
            fields: &fields,
            offset,
            limit: 200,
        })?;
        if batch.is_empty() {
            break;
        }
        offset += batch.len() as i64;
        for record in batch {
            let mut visible = !office || record["companyScope"] == actor.company;
            for action in actions {
                visible &= if matches!(resource, "office.supplies" | "office.rooms") {
                    super::super::handling::resource_access(
                        tx, actor, resource, action, &record, false,
                    )?
                } else {
                    auth::visible(actor, resource, action, &record)
                };
            }
            if visible {
                result.push(record);
            }
        }
        if offset >= total {
            break;
        }
    }
    Ok(result)
}
