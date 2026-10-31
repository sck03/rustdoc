//! One snapshot for the approval centre; reuse the paginated request contract and authorization.
use super::*;
pub(super) fn list(tx: &Connection, actor: &Actor) -> Result<Value> {
    let mut entries = vec![];
    let authority = super::queue_authority::QueueAuthority::new(tx, actor)?;
    for kind in [
        "personnel",
        "leave",
        "overtime",
        "expense",
        "travel",
        "purchase",
        "general",
    ] {
        let resource = approval::resource(kind).ok_or_else(|| invalid("未知审批业务。"))?;
        if auth::authorize(actor, resource, "view").is_err() {
            continue;
        }
        let approve = auth::authorize(actor, resource, "approve").is_ok();
        let complete = kind != "personnel" && auth::authorize(actor, resource, "complete").is_ok();
        for handling in [false, true] {
            if handling && !complete {
                continue;
            }
            let finance = handling && kind == "expense";
            let query = vec![
                ("mineOnly", (!handling && !approve).to_string()),
                (
                    "status",
                    if handling { "Approved" } else { "Pending" }.into(),
                ),
                ("approvalsOnly", (!handling && approve).to_string()),
                (
                    if finance {
                        "financeOnly"
                    } else {
                        "handlingOnly"
                    },
                    handling.to_string(),
                ),
                ("pageNumber", "1".into()),
                ("pageSize", "5".into()),
            ];
            let page = queries::list_with(
                tx,
                actor,
                &json!({"kind":kind,"resource":resource}),
                &query,
                Some(&authority),
                SUMMARY_FIELDS,
            )?;
            entries.push(json!({"kind":kind,"finance":finance,"handling":handling && !finance,"status":if handling { "Approved" } else { "Pending" },"page":summary(page)}));
        }
    }
    for (kind, request_kind, resource) in [
        ("rooms", "bookings", "office.rooms"),
        ("supplies", "supply-requests", "office.supplies"),
    ] {
        if auth::authorize(actor, resource, "view").is_err() {
            continue;
        }
        let approve = auth::authorize(actor, resource, "approve").is_ok();
        let states = if kind == "rooms" {
            [
                ("Pending", "approve"),
                ("Approved", "issue"),
                ("InUse", "return"),
            ]
        } else {
            [
                ("Pending", "approve"),
                ("Approved", "issue"),
                ("Issued", "return"),
            ]
        };
        for (status, action) in states {
            let handling = action != "approve";
            if handling && auth::authorize(actor, resource, action).is_err() {
                continue;
            }
            let query = [
                ("status", status.into()),
                ("mineOnly", (!handling && !approve).to_string()),
                ("approvalsOnly", (!handling && approve).to_string()),
                ("handlingOnly", handling.to_string()),
                ("pageNumber", "1".into()),
                ("pageSize", "5".into()),
            ];
            let page = super::super::office_queries::list_with(
                tx,
                actor,
                request_kind,
                &query,
                Some(&authority),
                SUMMARY_FIELDS,
            )?;
            entries.push(json!({"kind":kind,"finance":false,"handling":handling,"status":status,"page":summary(page)}));
        }
    }
    Ok(json!({"entries":entries}))
}

const SUMMARY_FIELDS: &[(&str, &str)] = &[
    ("companyScope", "companyScope"),
    ("departmentId", "departmentId"),
    ("ownerUserId", "ownerUserId"),
    ("approvalPlan", "approvalPlan"),
    ("id", "id"),
    ("employeeId", "employeeId"),
    ("employeeNumber", "employeeNumber"),
    ("employeeName", "employeeName"),
    ("applicantName", "applicantName"),
    ("title", "title"),
    ("supplyName", "supplyName"),
    ("quantity", "quantity"),
    ("unit", "unit"),
];

fn summary(mut page: Value) -> Value {
    page["items"] = json!(page["items"].as_array().into_iter().flatten().map(|row| {
        let name = if row["employeeName"].is_string() { &row["employeeName"] } else { &row["applicantName"] };
        let title = if row["supplyName"].is_string() { format!("{} · {} {}", text(row,"supplyName"), row["quantity"], text(row,"unit")) } else { text(row,"title") };
        json!({"id":row["id"],"employeeId":row["employeeId"],"employeeNumber":row["employeeNumber"],"employeeName":name,"title":title})
    }).collect::<Vec<_>>());
    page
}
