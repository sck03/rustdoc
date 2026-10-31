//! Canonical organization and appointment writes. The records payload contains
//! display attributes only; relational facts are projected into a read-only view.
use crate::{Adapter, Error, Result, sql};
use serde_json::{Value, json};

pub(crate) fn supports(kind: &str) -> bool {
    matches!(
        kind,
        "companies" | "departments" | "people" | "users" | "permission-templates" | "unit-access"
    )
}
pub(crate) fn project(db: &(impl Adapter + ?Sized), id: i64) -> Result<()> {
    let mut s = Write::new(db);
    let id = s.number(id);
    let version = "(SELECT b.scheme_version FROM permission_bindings b WHERE b.principal_id=r.id AND b.principal_kind=CASE r.kind WHEN 'users' THEN 'Account' ELSE 'Unit' END)";
    let body = if s.pg {
        format!(
            "jsonb_set(r.body,'{{permissionSchemeVersion}}',COALESCE(to_jsonb({version}),'null'::jsonb),true)"
        )
    } else {
        format!("json_set(r.body,'$.permissionSchemeVersion',{version})")
    };
    s.execute(db,format!("INSERT INTO identity_projection(id,body) SELECT r.id,CASE WHEN r.kind IN('users','unit-access') THEN {body} ELSE r.body END FROM identity_facts r WHERE r.id={id} ON CONFLICT(id) DO UPDATE SET body=excluded.body"))
}
pub(crate) fn atomic<T>(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    work: impl FnOnce() -> Result<T>,
) -> Result<T> {
    if !supports(kind) {
        return work();
    }
    db.execute_statement("SAVEPOINT identity_write", &[])?;
    match work() {
        Ok(value) => {
            db.execute_statement("RELEASE SAVEPOINT identity_write", &[])?;
            Ok(value)
        }
        Err(cause) => {
            crate::operation::without_control(|| {
                db.execute_statement("ROLLBACK TO SAVEPOINT identity_write", &[])?;
                db.execute_statement("RELEASE SAVEPOINT identity_write", &[])?;
                Ok::<_, Error>(())
            })?;
            Err(cause)
        }
    }
}
pub(crate) fn document(kind: &str, body: &Value) -> Value {
    let mut body = body.clone();
    let keys: &[&str] = match kind {
        "companies" => &[
            "code",
            "name",
            "isActive",
            "parentCompanyCode",
            "unitKind",
            "executiveAppointmentId",
            "additionalLeaders",
        ],
        "departments" => &[
            "code",
            "name",
            "isActive",
            "companyCode",
            "parentCode",
            "managerAppointmentId",
            "additionalLeaders",
        ],
        "people" => &[
            "organization",
            "account",
            "status",
            "employeeNumber",
            "employmentType",
            "hireDate",
            "probationEndsOn",
            "contractEndsOn",
            "jobTitle",
        ],
        "users" => &[
            "permissionSchemeVersion",
            "username",
            "fullName",
            "role",
            "isActive",
            "permissionTemplateId",
            "permissionGrants",
            "useRoleDefaults",
        ],
        "unit-access" => &[
            "permissionSchemeVersion",
            "userId",
            "companyCode",
            "appointmentId",
            "mode",
            "isActive",
            "permissionTemplateId",
            "authorityRoleId",
            "permissionGrants",
        ],
        "permission-templates" => &["ownerUnitId", "code"],
        _ => &[],
    };
    if let Some(value) = body.as_object_mut() {
        for key in keys {
            value.remove(*key);
        }
    }
    if kind == "people" {
        if let Some(profile) = body["profile"].as_object_mut() {
            profile.remove("fullName");
        }
        if let Some(employee) = body["employee"].as_object_mut() {
            for key in [
                "fullName",
                "employeeNumber",
                "status",
                "hireDate",
                "employmentType",
                "jobTitle",
                "departmentId",
            ] {
                employee.remove(key);
            }
        }
        body.as_object_mut().map(|v| v.remove("fullName"));
    }
    body
}
struct Write {
    pg: bool,
    values: Vec<String>,
}
impl Write {
    fn new(db: &(impl Adapter + ?Sized)) -> Self {
        Self {
            pg: db.provider() == "PostgreSQL",
            values: vec![],
        }
    }
    fn text(&mut self, v: impl ToString) -> String {
        sql::bind(&mut self.values, v, self.pg)
    }
    fn number(&mut self, v: impl ToString) -> String {
        format!("CAST({} AS BIGINT)", self.text(v))
    }
    fn optional_number(&mut self, v: Option<i64>) -> String {
        format!(
            "CAST(NULLIF({},'') AS BIGINT)",
            self.text(v.map(|v| v.to_string()).unwrap_or_default())
        )
    }
    fn optional_text(&mut self, v: Option<&str>) -> String {
        format!("NULLIF({},'')", self.text(v.unwrap_or("")))
    }
    fn json(&mut self, v: &Value) -> Result<String> {
        let p = self.text(serde_json::to_string(v)?);
        Ok(if self.pg {
            format!("CAST({p} AS JSONB)")
        } else {
            p
        })
    }
    fn unit(&mut self, code: &str) -> String {
        let code = self.text(code);
        format!("(SELECT id FROM organization_units WHERE code={code})")
    }
    fn department(&mut self, company: &str, code: &str) -> String {
        let c = self.text(company);
        let d = self.text(code);
        format!(
            "(SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code={c} AND d.code={d})"
        )
    }
    fn execute(self, db: &(impl Adapter + ?Sized), statement: String) -> Result<()> {
        db.execute_statement(&statement, &self.values)?;
        Ok(())
    }
}
fn text<'a>(v: &'a Value, key: &str) -> &'a str {
    v[key].as_str().unwrap_or("")
}
fn upsert(
    db: &(impl Adapter + ?Sized),
    table: &str,
    key: &str,
    columns: &[&str],
    parameters: Vec<String>,
    s: Write,
) -> Result<()> {
    let changes = columns
        .iter()
        .filter(|c| **c != key)
        .map(|c| format!("{c}=excluded.{c}"))
        .collect::<Vec<_>>()
        .join(",");
    s.execute(
        db,
        format!(
            "INSERT INTO {table}({}) VALUES({}) ON CONFLICT({key}) DO UPDATE SET {changes}",
            columns.join(","),
            parameters.join(",")
        ),
    )
}
pub(crate) fn persist(
    db: &(impl Adapter + ?Sized),
    id: i64,
    kind: &str,
    body: &Value,
) -> Result<()> {
    if !supports(kind) {
        return Ok(());
    }
    crate::operation::check()?;
    match kind {
        "companies" | "departments" => {
            let mut s = Write::new(db);
            let code = s.text(text(body, "code"));
            let ident = s.number(id);
            if kind == "companies" {
                let parent = if text(body, "parentCompanyCode").is_empty() {
                    "NULL".into()
                } else {
                    s.unit(text(body, "parentCompanyCode"))
                };
                let unit_kind = s.text(body["unitKind"].as_str().unwrap_or("Company"));
                upsert(
                    db,
                    "organization_units",
                    "id",
                    &["id", "code", "parent_id", "unit_kind"],
                    vec![ident, code, parent, unit_kind],
                    s,
                )?;
            } else {
                let company = text(body, "companyCode");
                let unit = s.unit(company);
                let parent = if text(body, "parentCode").is_empty() {
                    "NULL".into()
                } else {
                    s.department(company, text(body, "parentCode"))
                };
                upsert(
                    db,
                    "organization_departments",
                    "id",
                    &["id", "unit_id", "code", "parent_id"],
                    vec![ident, unit, code, parent],
                    s,
                )?;
            }
            let mut s = Write::new(db);
            let p = vec![
                s.number(id),
                s.text(text(body, "name")),
                s.number(i64::from(body["isActive"] == true)),
            ];
            upsert(
                db,
                "organization_labels",
                "id",
                &["id", "name", "is_active"],
                p,
                s,
            )?;
            let mut s = Write::new(db);
            let ident = s.number(id);
            s.execute(
                db,
                format!("DELETE FROM organization_duties WHERE organization_id={ident}"),
            )?;
            let primary = body
                .get(if kind == "companies" {
                    "executiveAppointmentId"
                } else {
                    "managerAppointmentId"
                })
                .and_then(Value::as_str);
            let leaders = body["additionalLeaders"]
                .as_array()
                .cloned()
                .unwrap_or_default();
            let assignments = primary
                .into_iter()
                .map(|id| (id.to_owned(), "Primary", json!([])))
                .chain(leaders.iter().map(|v| {
                    (
                        text(v, "appointmentId").to_owned(),
                        "Delegate",
                        v["workflowKinds"].clone(),
                    )
                }));
            for (appointment, role, kinds) in assignments {
                let mut s = Write::new(db);
                let p = vec![
                    s.number(id),
                    s.text(appointment),
                    s.text(role),
                    s.json(&kinds)?,
                ];
                s.execute(db,format!("INSERT INTO organization_duties(organization_id,appointment_id,duty_kind,workflow_kinds) VALUES({})",p.join(",")))?;
            }
        }
        "people" => {
            let org = &body["organization"];
            let mut s = Write::new(db);
            let p = vec![
                s.number(id),
                s.text(
                    body["profile"]["fullName"]
                        .as_str()
                        .unwrap_or_else(|| text(body, "fullName")),
                ),
                s.text(text(body, "employeeNumber")),
                s.text(text(body, "status")),
                s.text(text(body, "employmentType")),
                s.text(text(body, "hireDate")),
                s.optional_text(body["probationEndsOn"].as_str()),
                s.optional_text(body["contractEndsOn"].as_str()),
            ];
            upsert(
                db,
                "persons",
                "id",
                &[
                    "id",
                    "full_name",
                    "employee_number",
                    "status",
                    "employment_type",
                    "hire_date",
                    "probation_ends_on",
                    "contract_ends_on",
                ],
                p,
                s,
            )?;
            let primary = json!({"id":org["primaryAssignmentId"],"companyCode":body["companyScope"],"departmentId":body["departmentId"],"jobTitle":body["jobTitle"],"startsOn":if text(org,"primaryStartsOn").is_empty(){body["hireDate"].clone()}else{org["primaryStartsOn"].clone()},"endsOn":"","managerAssignmentId":org["managerAssignmentId"],"functionalManagerAssignmentIds":org["functionalManagerAssignmentIds"]});
            let mut appointments = vec![primary];
            appointments.extend(
                body["organization"]["additionalAssignments"]
                    .as_array()
                    .cloned()
                    .unwrap_or_default(),
            );
            // Remove only absent appointments; reference guards and deferred FKs
            // preserve actual history. Unchanged rows retain their stable identity.
            let mut s = Write::new(db);
            let person = s.number(id);
            let ids = appointments
                .iter()
                .map(|a| s.text(text(a, "id")))
                .collect::<Vec<_>>()
                .join(",");
            s.execute(db,format!("DELETE FROM personnel_memberships WHERE person_id={person} AND id NOT IN ({ids})"))?;
            let mut s = Write::new(db);
            let person = s.number(id);
            s.execute(db,format!("DELETE FROM reporting_relations WHERE appointment_id IN (SELECT id FROM personnel_memberships WHERE person_id={person})"))?;
            let mut s = Write::new(db);
            let person = s.number(id);
            s.execute(db,format!("UPDATE personnel_memberships SET is_primary=0 WHERE person_id={person} AND is_primary=1"))?;
            for (index, a) in appointments.iter().enumerate() {
                crate::operation::check()?;
                let mut s = Write::new(db);
                let company = text(a, "companyCode");
                let department = text(a, "departmentId");
                let p = vec![
                    s.number(id),
                    s.text(company),
                    s.text(department),
                    s.text(text(a, "jobTitle")),
                    s.text(text(a, "startsOn")),
                    s.text(text(a, "endsOn")),
                    s.number(i64::from(index == 0)),
                    s.text(text(a, "id")),
                    s.unit(company),
                    s.department(company, department),
                    // Re-resolve the position in sync_person before this transaction commits.
                    // A previous department's position cannot accompany the new appointment facts.
                    "NULL".into(),
                ];
                upsert(
                    db,
                    "personnel_memberships",
                    "id",
                    &[
                        "person_id",
                        "company",
                        "department",
                        "job_title",
                        "starts_on",
                        "ends_on",
                        "is_primary",
                        "id",
                        "unit_id",
                        "department_id",
                        "position_id",
                    ],
                    p,
                    s,
                )?;
            }
            for a in &appointments {
                let relations = a["managerAssignmentId"]
                    .as_str()
                    .into_iter()
                    .map(|id| (id, "Line"))
                    .chain(
                        a["functionalManagerAssignmentIds"]
                            .as_array()
                            .into_iter()
                            .flatten()
                            .filter_map(|id| id.as_str().map(|id| (id, "Functional"))),
                    );
                for (manager, kind) in relations {
                    let mut s = Write::new(db);
                    let p = vec![s.text(text(a, "id")), s.text(manager), s.text(kind)];
                    s.execute(
                        db,
                        format!("INSERT INTO reporting_relations VALUES({})", p.join(",")),
                    )?;
                }
            }
            let mut s = Write::new(db);
            let person = s.number(id);
            s.execute(
                db,
                format!("DELETE FROM personnel_accounts WHERE person_id={person}"),
            )?;
            if let Some(user) = body["account"]["id"].as_i64() {
                let mut s = Write::new(db);
                let p = vec![s.number(id), s.number(user)];
                s.execute(
                    db,
                    format!("INSERT INTO personnel_accounts VALUES({})", p.join(",")),
                )?;
            }
        }
        "users" => {
            let mut s = Write::new(db);
            let p = vec![
                s.number(id),
                s.text(text(body, "username")),
                s.text(text(body, "fullName")),
                s.text(text(body, "role")),
                s.number(i64::from(body["isActive"] == true)),
                s.optional_number(body["permissionTemplateId"].as_i64()),
                s.number(i64::from(body["useRoleDefaults"] == true)),
                s.text(if body["permissionGrants"].is_array() {
                    "Independent"
                } else {
                    "Template"
                }),
            ];
            upsert(
                db,
                "accounts",
                "id",
                &[
                    "id",
                    "username",
                    "full_name",
                    "role_code",
                    "is_active",
                    "template_id",
                    "use_role_defaults",
                    "source_mode",
                ],
                p,
                s,
            )?;
            let mut s = Write::new(db);
            let account = s.number(id);
            s.execute(
                db,
                format!("DELETE FROM account_grants WHERE account_id={account}"),
            )?;
            for g in body["permissionGrants"].as_array().into_iter().flatten() {
                let mut s = Write::new(db);
                let p = vec![
                    s.number(id),
                    s.text(text(g, "resourceKey")),
                    s.text(text(g, "action")),
                    s.text(text(g, "dataScope")),
                ];
                s.execute(
                    db,
                    format!("INSERT INTO account_grants VALUES({})", p.join(",")),
                )?;
            }
        }
        "permission-templates" => {
            let mut s = Write::new(db);
            let p = vec![
                s.number(id),
                s.optional_number(body["ownerUnitId"].as_i64()),
                s.text(text(body, "code")),
            ];
            upsert(
                db,
                "permission_schemes",
                "id",
                &["id", "owner_unit_id", "code"],
                p,
                s,
            )?;
        }
        "unit-access" => {
            let mut s = Write::new(db);
            let p = vec![
                s.number(id),
                s.number(
                    body["userId"]
                        .as_i64()
                        .ok_or_else(|| Error::unavailable("身份缺少账号。"))?,
                ),
                s.unit(text(body, "companyCode")),
                s.optional_text(body["appointmentId"].as_str()),
                s.text(text(body, "mode")),
                s.number(i64::from(body["isActive"] == true)),
                s.optional_number(body["permissionTemplateId"].as_i64()),
                s.optional_number(body["authorityRoleId"].as_i64()),
                s.text(if body["permissionGrants"].is_array() {
                    "Independent"
                } else {
                    "Template"
                }),
            ];
            upsert(
                db,
                "unit_access_roles",
                "id",
                &[
                    "id",
                    "user_id",
                    "unit_id",
                    "appointment_id",
                    "mode",
                    "is_active",
                    "template_id",
                    "authority_role_id",
                    "source_mode",
                ],
                p,
                s,
            )?;
            let mut s = Write::new(db);
            let role = s.number(id);
            s.execute(
                db,
                format!("DELETE FROM unit_access_grants WHERE role_id={role}"),
            )?;
            for g in body["permissionGrants"].as_array().into_iter().flatten() {
                let mut s = Write::new(db);
                let p = vec![
                    s.number(id),
                    s.text(text(g, "resourceKey")),
                    s.text(text(g, "action")),
                    s.text(text(g, "dataScope")),
                ];
                s.execute(
                    db,
                    format!("INSERT INTO unit_access_grants VALUES({})", p.join(",")),
                )?;
            }
        }
        _ => {}
    }
    if kind == "people" {
        crate::foundation::sync_person(db, id, body)?;
    }
    crate::permission_versions::sync(db, id, kind, body)?;
    project(db, id)?;
    Ok(())
}
