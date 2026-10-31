//! Security suspension is immediate even when business handover is unfinished.
use super::super::{command_receipts, error::conflict};
use super::*;

pub(in crate::engine) fn clear_grants(identity: &mut Value) {
    identity["isActive"] = json!(false);
    identity["permissionGrants"] = json!([]);
    identity["permissionTemplateId"] = Value::Null;
    identity["permissionSchemeVersion"] = Value::Null;
}

/// Revocation is durable: re-enabling an account cannot revive an old role or proxy.
pub(in crate::engine) fn revoke_related(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    action: &str,
) -> Result<()> {
    for mut role in store::matching(tx, KIND, &[("userId", id.to_string())], &[])? {
        let role_id = role["id"]
            .as_i64()
            .ok_or_else(|| unavailable("单位身份编号损坏。"))?;
        clear_grants(&mut role);
        role["expectedVersion"] = role["versionNumber"].clone();
        let scope = role.clone();
        store::save_in_scope(tx, KIND, role_id, role, None, actor, action, Some(&scope))?;
    }
    super::super::oa::revoke_delegations(tx, actor, id)
}
pub(super) fn handle(store: &store::Store, actor: &Actor, body: &Value) -> Result<Value> {
    let id = export_doc_domain::identity::StableId::parse(body["accountId"].as_str().unwrap_or(""))
        .map_err(invalid)?
        .number();
    super::super::records::required(body, "note", "安全停用原因", 500)?;
    store.transaction_as(actor,|tx,actor| {
        auth::authorize(actor,"system.users","manage")?;
        let mut user=store::get(tx,"users",id)?;
        if command_receipts::prepare(tx,actor,&mut user,"emergency-disable-account",body)? {
            return tx.command_receipt(actor.id,&actor.context,&text(body,"commandKey"))?.map(|r|r["result"].clone()).ok_or_else(||unavailable("停用操作回执缺失。"));
        }
        store::check_version(&user,store::expected(body))?;
        if user["role"]=="Admin"&&user["isActive"]==true&&store::all(tx,"users")?.iter().filter(|u|u["role"]=="Admin"&&u["isActive"]==true).count()<=1{return Err(conflict("停用此维护管理员前须建立另一位启用的维护管理员。"));}
        let clearance=super::super::office_queries::account_clearance(tx,&user)?;
        let receipt=user[command_receipts::FIELD].clone();user.as_object_mut().map(|v|v.remove(command_receipts::FIELD));
        clear_grants(&mut user);user["expectedVersion"]=user["versionNumber"].clone();
        let username=text(&user,"username");let scope=user.clone();
        let saved=store::save_in_scope(tx,"users",id,user,Some(username),actor,"security-suspend",Some(&scope))?;
        revoke_related(tx, actor, id, "security-suspend")?;
        let result=json!({"accountId":id.to_string(),"versionNumber":saved["versionNumber"],"isActive":false,"handoverRequired":clearance["isClear"]!=true});
        tx.put_command_receipt(&receipt,&result)?;
        Ok(result)
    })
}
