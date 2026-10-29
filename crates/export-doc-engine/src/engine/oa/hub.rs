//! One snapshot for the approval centre; reuse the paginated request contract and authorization.
use super::*;
pub(super) fn list(tx: &Connection, actor: &Actor) -> Result<Value> {
    let mut entries = vec![];
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
            let page = queries::list(tx, actor, &json!({"kind":kind,"resource":resource}), &query)?;
            entries.push(
                json!({"kind":kind,"finance":finance,"handling":handling && !finance,"page":page}),
            );
        }
    }
    Ok(json!({"entries":entries}))
}
