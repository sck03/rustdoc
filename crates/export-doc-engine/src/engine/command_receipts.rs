//! Command retries are bound to the actor, identity, target and exact request.
use super::{
    error::{Result, conflict, invalid},
    records::text,
    store::{Actor, Connection},
};
use serde_json::{Value, json};
pub(super) const FIELD: &str = "__commandReceipt";
pub(super) fn prepare(
    tx: &Connection,
    actor: &Actor,
    row: &mut Value,
    command: &str,
    body: &Value,
) -> Result<bool> {
    let key = text(body, "commandKey");
    if !(8..=128).contains(&key.len())
        || !key
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b"-_:".contains(&b))
    {
        return Err(invalid(
            "操作编号须为 8–128 位字母、数字或 -_:；重试须保留相同编号。",
        ));
    }
    let target = row["id"]
        .as_i64()
        .ok_or_else(|| invalid("命令目标无效。"))?;
    let mut request = body.clone();
    request.sort_all_objects();
    let digest = super::media::digest(&serde_json::to_vec(&request)?);
    if let Some(receipt) = tx.command_receipt(actor.id, &actor.context, &key)? {
        if receipt["command"] != command
            || receipt["targetId"] != target
            || receipt["requestDigest"] != digest
        {
            return Err(conflict(
                "同一操作编号已用于不同命令或正文，请核对提交结果。",
            ));
        }
        return Ok(true);
    }
    row[FIELD] = json!({"actorId":actor.id,"contextId":actor.context,"commandKey":key,"command":command,"targetId":target,"requestDigest":digest,"createdAt":super::store::timestamp()});
    Ok(false)
}
