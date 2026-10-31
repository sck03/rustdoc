//! A business signature always identifies the linked natural person.
use super::*;
pub(in crate::engine) fn person(tx: &Connection, actor: &Actor) -> Result<Value> {
    if actor.admin {
        return Err(error(
            403,
            "平台维护身份不能作为普通业务签署人，请使用关联人员的业务账号。",
        ));
    }
    super::super::personnel_accounts::person_for_account(tx, actor.id)?
        .filter(|p| matches!(p["status"].as_str(), Some("Active" | "Probation")))
        .ok_or_else(|| error(403, "审批身份必须关联在职人员。"))
}
pub(in crate::engine) fn person_id(tx: &Connection, actor: &Actor) -> Result<i64> {
    records::positive(&person(tx, actor)?, "id", "签署人员")
}
