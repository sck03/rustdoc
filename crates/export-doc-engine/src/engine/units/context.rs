use super::*;

fn key(id: i64, company: &str) -> String {
    format!("unit:{id}:{company}")
}
/// Reuse data only inside the caller's current transaction; expiry and revoked
/// permissions are resolved afresh on every request.
struct Snapshot {
    units: Vec<Value>,
    ancestry: std::collections::BTreeMap<String, Vec<String>>,
    user: Option<Value>,
    person: Option<Option<Value>>,
    profiles: std::collections::HashMap<i64, super::super::account_permissions::Profile>,
}
impl Snapshot {
    fn new(tx: &Connection) -> Result<Self> {
        let units = store::all(tx, "companies")?;
        let ancestry = tree_from(&units)?;
        Ok(Self {
            units,
            ancestry,
            user: None,
            person: None,
            profiles: Default::default(),
        })
    }
}
fn role_actor(
    tx: &Connection,
    base: &Actor,
    row: &Value,
    unit: &str,
    snapshot: &mut Snapshot,
) -> Result<Actor> {
    if row["userId"] != base.id || !window(row)? || !covers(row, unit, &snapshot.ancestry) {
        return Err(error(403, "此单位身份已失效或不属于当前账号。"));
    }
    let unit_record = snapshot
        .units
        .iter()
        .find(|u| u["code"] == unit && u["isActive"] == true)
        .ok_or_else(|| error(403, "单位不存在或已停用。"))?;
    let mode = text(row, "mode");
    if mode != "Administration" && unit_record["unitKind"] == "Group" {
        return Err(error(403, "集团目录不是业务归属单位。"));
    }
    let mut actor = base.clone();
    actor.context = key(
        row["id"]
            .as_i64()
            .ok_or_else(|| unavailable("授权编号损坏。"))?,
        unit,
    );
    actor.company = unit.into();
    actor.department.clear();
    actor.departments.clear();
    actor.admin = false;
    actor.oa_assigned_only = row["oaAssignedOnly"] == true || mode == "Collaboration";
    actor.grants.clear();
    if mode == "Administration" {
        let management: Management = serde_json::from_value(row["management"].clone())?;
        for (allowed, resource) in [
            (management.analytics, "organization.analytics"),
            (management.organization, "organization.directory"),
            (management.appointments, "organization.appointments"),
            (management.workflows, "organization.workflows"),
            (management.roles, "organization.roles"),
        ] {
            if allowed {
                actor.grants.push(Grant {
                    resource_key: resource.into(),
                    action: "manage".into(),
                    data_scope: "company".into(),
                });
            }
        }
        // Workflow administration includes only public organization lookups.
        if management.organization || management.workflows || management.appointments {
            actor.grants.push(Grant {
                resource_key: "organization.directory".into(),
                action: "view".into(),
                data_scope: "company".into(),
            });
        }
    } else {
        if snapshot.person.is_none() {
            snapshot.person = Some(super::super::personnel_accounts::person_for_account(
                tx, base.id,
            )?);
        }
        let person = snapshot
            .person
            .as_ref()
            .and_then(Option::as_ref)
            .filter(|p| matches!(p["status"].as_str(), Some("Active" | "Probation")))
            .ok_or_else(|| error(403, "单位业务身份需要关联在职人员。"))?;
        if mode == "Business" {
            let appointment = super::super::personnel_organization::appointment_by_id(
                tx,
                person,
                unit,
                &text(row, "appointmentId"),
            )?;
            actor.department = appointment.department_id;
            actor.departments =
                super::super::personnel_organization::departments_in(tx, person, unit)?;
        } else if mode != "Collaboration" {
            return Err(unavailable("单位身份类型损坏。"));
        }
        if snapshot.user.is_none() {
            snapshot.user = Some(store::get(tx, "users", base.id)?);
        }
        let user = snapshot.user.as_ref().unwrap();
        let id = row["id"]
            .as_i64()
            .ok_or_else(|| unavailable("授权编号损坏。"))?;
        if !snapshot.profiles.contains_key(&id) {
            snapshot.profiles.insert(
                id,
                super::role_permissions::profile_in(tx, row, &snapshot.ancestry)?,
            );
        }
        let profile = snapshot.profiles[&id].clone();
        let mut disabled = profile.disabled;
        disabled.extend(super::super::account_permissions::modules(&user)?);
        actor.oa_assigned_only |= profile.assigned_only || user["oaAssignedOnly"] == true;
        actor.grants = export_doc_domain::permissions::profile::resolve(&profile.direct, &disabled)
            .map_err(unavailable)?
            .into_iter()
            .filter(|g| actor.edition.allows(&g.resource_key))
            .collect();
        if actor.grants.iter().any(|g| {
            g.action == "view"
                && super::super::oa::approval::resource_for_collaboration(&g.resource_key)
        }) && !disabled.iter().any(|m| m == "office.notifications")
        {
            actor.grants.push(Grant {
                resource_key: "office.notifications".into(),
                action: "view".into(),
                data_scope: "own".into(),
            });
        }
        if mode == "Business" {
            let mut scope = user.clone();
            scope["companyScope"] = json!(unit);
            scope["departmentId"] = json!(actor.department);
            actor.departments = super::super::organization_scope::departments_for_person(
                tx,
                &scope,
                person,
                actor.grants.iter().any(|g| g.data_scope == "department"),
            )?;
        }
    }
    actor
        .scope_rules
        .extend(super::super::scope_policies::rules(
            tx,
            "Unit",
            row["id"]
                .as_i64()
                .ok_or_else(|| unavailable("单位身份编号缺失。"))?,
        )?);
    super::super::scope_policies::restrict(&mut actor);
    Ok(actor)
}

pub(in crate::engine) fn resolve(
    tx: &Connection,
    id: i64,
    edition: permissions::ProductEdition,
    context: &str,
) -> Result<Actor> {
    let mut actor = auth::current_actor_in(tx, id, edition)?;
    if context.is_empty() {
        return Ok(actor);
    }
    if let Some(code) = context.strip_prefix("company:") {
        if !actor.admin {
            return Err(error(403, "只有系统管理员可以选择平台管理单位。"));
        }
        let selected = company(tx, code)?;
        if selected["unitKind"] == "Group" {
            return Err(error(403, "请选择经营单位。"));
        }
        actor.company = code.into();
        actor.department.clear();
        actor.departments.clear();
        actor.context = context.into();
        return Ok(actor);
    }
    let (id, company) = context
        .strip_prefix("unit:")
        .and_then(|v| v.split_once(':'))
        .ok_or_else(|| error(403, "单位身份无效。"))?;
    let id = id
        .parse::<i64>()
        .map_err(|_| error(403, "单位身份无效。"))?;
    let record = tx
        .get(KIND, id)?
        .ok_or_else(|| error(403, "单位身份已撤销。"))?;
    role_actor(tx, &actor, &record, company, &mut Snapshot::new(tx)?)
}

pub(in crate::engine) fn contexts(tx: &Connection, actor: &Actor) -> Result<Vec<Value>> {
    let base = auth::current_actor_in(tx, actor.id, actor.edition)?;
    let units = store::all(tx, "companies")?;
    let name = |code: &str| {
        units
            .iter()
            .find(|u| u["code"] == code)
            .map(|u| text(u, "name"))
            .unwrap_or_else(|| code.into())
    };
    let mut result = vec![
        json!({"id":"","companyCode":base.company,"companyName":name(&base.company),"name":"主身份","departmentId":base.department,"mode":"Primary"}),
    ];
    if !actor.edition.allows("office.people") {
        return Ok(result);
    }
    if base.admin {
        result.extend(units.iter().filter(|u| u["isActive"] == true && u["unitKind"] != "Group" && u["code"] != base.company).map(|u| json!({"id":format!("company:{}",text(u,"code")),"companyCode":u["code"],"companyName":u["name"],"name":"系统管理","departmentId":"","mode":"Administration"})));
    } else {
        let mut snapshot = Snapshot {
            ancestry: tree_from(&units)?,
            units,
            user: None,
            person: None,
            profiles: Default::default(),
        };
        for row in records(tx, base.id)? {
            if !window(&row)? {
                continue;
            }
            for (code, name) in snapshot
                .units
                .iter()
                .filter(|u| {
                    u["isActive"] == true && covers(&row, &text(u, "code"), &snapshot.ancestry)
                })
                .map(|u| (text(u, "code"), u["name"].clone()))
                .collect::<Vec<_>>()
            {
                match role_actor(tx, &base, &row, &code, &mut snapshot) {
                    Ok(a) => result.push(json!({"id":a.context,"companyCode":code,"companyName":name,"name":row["name"],"departmentId":a.department,"mode":row["mode"]})),
                    Err(e) if matches!(e.status,Some(400|403|404)) => {},
                    Err(e) => return Err(e),
                }
            }
        }
    }
    Ok(result)
}

pub(in crate::engine) fn actors_for_operation(
    tx: &Connection,
    id: i64,
    edition: permissions::ProductEdition,
    company: &str,
    resource: &str,
    action: &str,
) -> Result<Vec<Actor>> {
    Ok(business_actors(tx, id, edition, company)?
        .into_iter()
        .filter(|a| {
            auth::authorize(a, resource, "view").is_ok()
                && auth::authorize(a, resource, action).is_ok()
        })
        .collect())
}

pub(in crate::engine) fn business_actors(
    tx: &Connection,
    id: i64,
    edition: permissions::ProductEdition,
    company: &str,
) -> Result<Vec<Actor>> {
    let base = auth::current_actor_in(tx, id, edition)?;
    let mut result = vec![];
    if base.company == company {
        result.push(base.clone());
    }
    let rows = records(tx, id)?;
    if rows.is_empty() {
        return Ok(result);
    }
    let mut snapshot = Snapshot::new(tx)?;
    for row in rows {
        if row["mode"] == "Administration" {
            continue;
        }
        match role_actor(tx, &base, &row, company, &mut snapshot) {
            Ok(a) => result.push(a),
            Err(e) if matches!(e.status, Some(400 | 403 | 404)) => {}
            Err(e) => return Err(e),
        }
    }
    Ok(result)
}

pub(in crate::engine) fn for_operation(
    tx: &Connection,
    id: i64,
    edition: permissions::ProductEdition,
    company: &str,
    resource: &str,
    action: &str,
) -> Result<Actor> {
    actors_for_operation(tx, id, edition, company, resource, action)?
        .into_iter()
        .next()
        .ok_or_else(|| error(403, "账号在此单位没有有效的业务任职或协作授权。"))
}
