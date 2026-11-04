//! Original signatures remain independently pageable after exceptional execution revisions.
use super::*;
pub(super) fn list(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    query: &[(&str, String)],
) -> Result<Value> {
    let argument = |key| crate::engine::request_values::find(query, key).unwrap_or("");
    let number = |key| {
        argument(key)
            .parse::<i64>()
            .ok()
            .filter(|v| *v > 0)
            .ok_or_else(|| invalid("提交或执行代次须为正整数。"))
    };
    let revision = if argument("submissionNumber").is_empty() {
        records::positive(row, "submissionNumber", "提交版本")?
    } else {
        number("submissionNumber")?
    };
    let generation = if argument("generation").is_empty() {
        None
    } else {
        Some(number("generation")?)
    };
    let snapshot =
        submissions::checked(tx, row, submissions::list_revision_id(tx, row, revision)?)?;
    let full = super::materials::full(tx, actor, row, &snapshot)?;
    let (page, size) = paging(query)?;
    let mut result = tx.workflow_decisions(
        records::positive(row, "id", "申请")?,
        revision,
        generation,
        (page - 1) * size,
        size,
    )?;
    for decision in result["items"].as_array_mut().into_iter().flatten() {
        let node = decision
            .as_object_mut()
            .and_then(|d| d.remove("_node"))
            .ok_or_else(|| super::super::error::unavailable("历史节点定义缺失。"))?;
        let stage = decision["signature"]["stage"].clone();
        let mut signature = decision["signature"].clone();
        signature["stage"] = json!(0);
        let projected = super::materials::project(
            tx,
            actor,
            row,
            json!({"approvalPlan":{"nodes":[node],"steps":[signature]}}),
        )?;
        decision["signature"] = projected["approvalPlan"]["steps"][0].clone();
        decision["signature"]["stage"] = stage;
        if !full {
            decision["signature"]["verificationValues"] = json!({});
            decision["signature"]["note"] = json!("历史核验与详细意见按材料权限保留。");
        }
    }
    result["pageNumber"] = json!(page);
    result["pageSize"] = json!(size);
    Ok(result)
}
