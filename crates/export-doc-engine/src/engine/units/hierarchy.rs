use super::*;

fn analyze_company(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    previous: &Value,
    value: &mut Value,
) -> Result<Vec<Value>> {
    let code = text(value, "code");
    demand(actor, &code, "organization.directory")?;
    value["unitKind"] = json!(value["unitKind"].as_str().unwrap_or("Company"));
    if !actor.admin
        && (id == 0
            || previous["parentCompanyCode"] != value["parentCompanyCode"]
            || previous["unitKind"] != value["unitKind"])
    {
        return Err(error(403, "单位建立、类型和集团隶属由系统管理员维护。"));
    }
    let original = store::all(tx, "companies")?;
    let mut units = original.clone();
    units.retain(|u| u["id"] != id);
    units.push(value.clone());
    let typed = units
        .into_iter()
        .map(serde_json::from_value)
        .collect::<std::result::Result<Vec<export_doc_domain::units::Unit>, _>>()
        .map_err(|_| invalid("单位资料格式无效。"))?;
    let next_tree = export_doc_domain::units::ancestry(&typed).map_err(invalid)?;
    if value["unitKind"] == "Group" {
        if !value["executiveEmployeeId"].is_null()
            || !super::super::organization::leaders(value)?.is_empty()
            || tx.record_exists("departments", &[("companyCode", code.clone())])?
            || tx.record_exists("people", &[("companyScope", code.clone())])?
            || tx.record_exists("users", &[("companyScope", code.clone())])?
            || value["positions"].as_array().is_some_and(|p| !p.is_empty())
        {
            return Err(invalid(
                "集团目录只承接隶属与管理范围；人员、部门和业务应归属经营单位。",
            ));
        }
    }
    let mut impacts = vec![];
    if previous["parentCompanyCode"] != value["parentCompanyCode"]
        || previous["isActive"] != value["isActive"]
    {
        if id > 0 {
            let current_tree = tree(tx)?;
            for role in store::matching(tx, KIND, &[("isActive", "true".into())], &[])? {
                let current: std::collections::BTreeSet<_> = original
                    .iter()
                    .filter(|u| {
                        u["isActive"] == true && covers(&role, &text(u, "code"), &current_tree)
                    })
                    .map(|u| text(u, "code"))
                    .collect();
                let next: std::collections::BTreeSet<_> = typed
                    .iter()
                    .filter(|u| u.is_active && covers(&role, &u.code, &next_tree))
                    .map(|u| u.code.clone())
                    .collect();
                if current != next {
                    let unit_rows = |codes: Vec<String>| -> Vec<Value> {
                        codes
                            .into_iter()
                            .map(|code| {
                                let row =
                                    original.iter().find(|u| u["code"] == code).unwrap_or(value);
                                json!({"id":row["id"],"code":code,"name":row["name"]})
                            })
                            .collect()
                    };
                    impacts.push(json!({"roleId":role["id"],"roleName":role["name"],"userId":role["userId"],
                        "userName":store::get(tx,"users",role["userId"].as_i64().ok_or_else(||unavailable("授权账号引用损坏。"))?)?["fullName"],
                        "addedUnits":unit_rows(next.difference(&current).cloned().collect()),"removedUnits":unit_rows(current.difference(&next).cloned().collect())}));
                }
            }
        }
    }
    Ok(impacts)
}

pub(in crate::engine) fn validate_company(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    previous: &Value,
    value: &mut Value,
) -> Result<()> {
    let impacts = analyze_company(tx, actor, id, previous, value)?;
    if !impacts.is_empty() {
        let details = impacts
            .iter()
            .map(|impact| {
                let names = |key| {
                    impact[key]
                        .as_array()
                        .unwrap()
                        .iter()
                        .map(|u| text(u, "code"))
                        .collect::<Vec<_>>()
                        .join("、")
                };
                format!(
                    "{}：新增 [{}]，移出 [{}]",
                    text(impact, "roleName"),
                    names("addedUnits"),
                    names("removedUnits")
                )
            })
            .collect::<Vec<_>>()
            .join("；");
        return Err(super::super::error::conflict(format!(
            "隶属调整影响以下授权，请先交接并停用这些授权，调整后重新授权：{details}"
        )));
    }
    Ok(())
}

pub(in crate::engine) fn preview(
    store: &store::Store,
    actor: &Actor,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx,actor)?;
        let id = super::super::records::id(parameters)?;
        let previous = store::get(tx,"companies",id)?;
        demand(&actor,&text(&previous,"code"),"organization.directory")?;
        store::check_version(&previous,store::expected(body))?;
        let mut next = crate::contracts::overlay(previous.clone(),body);
        next["id"] = json!(id);
        if next["code"] != previous["code"] { return Err(invalid("单位代码不能通过隶属调整更换。")); }
        let impacts = analyze_company(tx,&actor,id,&previous,&mut next)?;
        Ok(json!({"unitId":id,"versionNumber":previous["versionNumber"],"requiresHandover":!impacts.is_empty(),"impacts":impacts}))
    })
}

pub(in crate::engine) fn companies(tx: &Connection, actor: &Actor) -> Result<Vec<Value>> {
    let mut result = store::all(tx, "companies")?;
    if !actor.admin {
        result.retain(|u| u["code"] == actor.company);
    }
    Ok(result)
}
