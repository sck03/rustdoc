//! Authorized, projected reads for cross-source worklists.
use super::super::{auth, error::Result, store::Actor};
use crate::engine::store::Connection;
use serde_json::Value;

pub(super) fn visit(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    resource: &str,
    actions: &[&str],
    office: bool,
    selection: &super::page::Selection<'_>,
    mut consume: impl FnMut(Value) -> Result<bool>,
) -> Result<()> {
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
        "kind",
        "approvalPlan",
        "applicantAccountId",
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
    // These queues have no deadline, so their display order is exactly physical id ASC.
    // Date/instant and dynamic workflow queues retain their full semantic evaluation.
    let source = if selection.include_summary {
        ""
    } else if kind == "invoices" {
        "invoice-review"
    } else {
        selection.source
    };
    let ordered = !selection.include_summary
        && matches!(
            source,
            "invoice-review" | "supply-approval" | "supply-collection"
        );
    let scan = export_doc_storage::WorklistScan {
        source,
        ascending: ordered,
    };
    let mut cursor = selection
        .after
        .filter(|after| ordered && after.source == source)
        .map(|after| after.id);
    let mut accepted = 0;
    loop {
        crate::operation::check()?;
        let (next, batch) = tx.scan_worklist(
            &export_doc_storage::ScopedQuery {
                kind,
                scopes: if scoped { &scopes } else { &[] },
                company: office.then_some(actor.company.as_str()),
                fields: &fields,
                limit: if ordered { selection.keep as i64 } else { 200 },
            },
            actor.id,
            &auth::data_scope(actor, "sales.customers", "view"),
            &scan,
            cursor,
        )?;
        if batch.is_empty() {
            break;
        }
        let count = batch.len();
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
                accepted += usize::from(consume(record)?);
                if ordered && accepted >= selection.keep {
                    return Ok(());
                }
            }
        }
        cursor = next;
        if count < if ordered { selection.keep } else { 200 } {
            break;
        }
    }
    Ok(())
}
