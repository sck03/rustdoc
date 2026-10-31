use super::*;
use export_doc_domain::identity::StableId;
pub(super) fn handle(
    store: &store::Store,
    actor: &Actor,
    meta: &Value,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let parameter = |key| {
        query
            .iter()
            .find(|(k, _)| *k == key)
            .map(|(_, v)| v.as_str())
            .unwrap_or("")
    };
    let principal = if meta["action"] == "list" {
        parameter("principalKind")
    } else {
        body["principalKind"].as_str().unwrap_or("")
    };
    let id = StableId::parse(if meta["action"] == "list" {
        parameter("principalId")
    } else {
        body["principalId"].as_str().unwrap_or("")
    })
    .map_err(invalid)?
    .number();
    let authorize = |tx: &Connection, actor: &Actor| -> Result<Value> {
        if !matches!(principal, "Account" | "Unit") {
            return Err(invalid("身份类型无效。"));
        }
        let row = store::get(
            tx,
            if principal == "Account" {
                "users"
            } else {
                KIND
            },
            id,
        )?;
        if principal == "Account" {
            if !actor.admin || row["role"] == "Admin" {
                return Err(error(
                    403,
                    "普通主账号的限制由平台管理员维护；维护管理员不参与普通限制。",
                ));
            }
        } else {
            demand(actor, &text(&row, "companyCode"), "organization.roles")?;
        }
        Ok(row)
    };
    if meta["action"] == "list" {
        return store.read(|tx| {
            let current = auth::refresh_actor_in(tx, actor)?;
            authorize(tx, &current)?;
            Ok(json!({"items":tx.access_restrictions(principal,id,false)?}))
        });
    }
    let body = body.clone();
    store.transaction_as(actor, |tx, actor| {
        let target = authorize(tx, actor)?;
        super::super::records::required(&body, "reason", "授权限制原因", 500)?;
        let resource = permissions::resource(&text(&body, "resourceKey"))
            .ok_or_else(|| invalid("权限资源无效。"))?;
        if !resource
            .actions
            .iter()
            .any(|a| a.key == body["action"].as_str().unwrap_or(""))
        {
            return Err(invalid("权限动作无效。"));
        }
        let mode = text(&body, "mode");
        if ![
            "Deny",
            "Unrestricted",
            "Units",
            "Departments",
            "Projects",
            "Participation",
        ]
        .contains(&mode.as_str())
        {
            return Err(invalid("权限范围类型无效。"));
        }
        if mode == "Participation" && !resource.workflow_resource
            || mode == "Projects" && !resource.workflow_resource
        {
            return Err(invalid("明确参与或项目范围只适用于 OA 申请材料。"));
        }
        let members = body["members"]
            .as_array()
            .filter(|m| m.len() <= 200)
            .ok_or_else(|| invalid("范围成员最多 200 项。"))?;
        let mut unique = std::collections::HashSet::new();
        for member in members {
            let member = StableId::parse(member.as_str().unwrap_or(""))
                .map_err(invalid)?
                .number();
            if !unique.insert(member) {
                return Err(invalid("范围成员不能重复。"));
            }
            let unit = match mode.as_str() {
                "Units" => store::get(tx, "companies", member)?,
                "Departments" => {
                    let department = store::get(tx, "departments", member)?;
                    company(tx, &text(&department, "companyCode"))?
                }
                "Projects" => {
                    let unit = company(tx, &text(&target, "companyCode"))?;
                    tx.foundation_get("projects", unit["id"].as_i64().unwrap_or(0), member)?
                        .ok_or_else(|| invalid("项目不属于当前单位。"))?;
                    unit
                }
                _ => return Err(invalid("此范围类型不接受成员。")),
            };
            if !actor.admin && unit["code"] != target["companyCode"] {
                return Err(error(403, "范围成员超出当前身份经营单位。"));
            }
        }
        if matches!(mode.as_str(), "Units" | "Departments" | "Projects") && members.is_empty() {
            return Err(invalid("请选择明确的范围成员。"));
        }
        let saved = tx.save_access_restriction(principal, id, &body)?;
        let user = if principal == "Account" {
            id
        } else {
            target["userId"]
                .as_i64()
                .ok_or_else(|| unavailable("身份账号引用损坏。"))?
        };
        let account = store::get(tx, "users", user)?;
        validate_user(tx, actor, &account)?;
        validate_issuer(tx, actor, user)?;
        invalidate_user(tx, actor, user)?;
        tx.append_audit_details(
            &export_doc_storage::AuditWrite {
                kind: "access-restrictions",
                record_id: StableId::parse(saved["id"].as_str().unwrap_or(""))
                    .map_err(unavailable)?
                    .number(),
                version: saved["versionNumber"].as_i64().unwrap_or(1),
                action: "restrict",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: &text(&body, "reason"),
            },
            &json!({"principalKind":principal,"principalId":id.to_string()}),
        )?;
        Ok(saved)
    })
}
