use super::*;
fn text<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or("")
}
fn id(value: &Value, key: &str) -> Result<i64> {
    value[key]
        .as_str()
        .and_then(|v| v.parse().ok())
        .filter(|v| *v > 0)
        .ok_or_else(|| Error::unavailable("人员基础实体编号无效。"))
}
fn insert(
    db: &(impl Adapter + ?Sized),
    table: &str,
    columns: &[&str],
    parameters: Vec<String>,
    s: Statement,
) -> Result<i64> {
    let result = format!(
        "{}('id',CAST(id AS TEXT)){}",
        object(s.pg),
        if s.pg { "::text" } else { "" }
    );
    let value = s.query(
        db,
        format!(
            "INSERT INTO {table}({}) VALUES({}) RETURNING {result}",
            columns.join(","),
            parameters.join(",")
        ),
    )?;
    id(&value, "id")
}
fn write(
    db: &(impl Adapter + ?Sized),
    table: &str,
    current: Option<i64>,
    unit: i64,
    expected: i64,
    columns: &[&str],
    parameters: Vec<String>,
    mut s: Statement,
) -> Result<i64> {
    if let Some(id) = current {
        let assignments = columns
            .iter()
            .zip(parameters)
            .map(|(c, p)| format!("{c}={p}"))
            .collect::<Vec<_>>()
            .join(",");
        let row = s.number(id);
        let unit = s.number(unit);
        let expected = s.number(expected);
        if s.execute(db,format!("UPDATE {table} SET {assignments},version=version+1 WHERE id={row} AND unit_id={unit} AND version={expected}"))?!=1{return Err(conflict("人员基础实体版本已改变或不属于当前单位。"));}
        Ok(id)
    } else {
        if expected != 0 {
            return Err(conflict("新建实体不能携带已保存版本。"));
        }
        let unit = s.number(unit);
        let mut columns = columns.to_vec();
        let mut parameters = parameters;
        columns.extend(["unit_id", "version"]);
        parameters.extend([unit, "1".into()]);
        insert(db, table, &columns, parameters, s)
    }
}
fn employee_identity(
    db: &(impl Adapter + ?Sized),
    person: i64,
    unit: i64,
    employee_number: &str,
) -> Result<i64> {
    let mut s = Statement::new(db);
    let person = s.number(person);
    let u = s.number(unit);
    let number = s.text(employee_number);
    let result = format!(
        "{}('id',CAST(id AS TEXT)){}",
        object(s.pg),
        if s.pg { "::text" } else { "" }
    );
    let identity=s.query(db,format!("INSERT INTO employee_identities(person_id,unit_id,employee_number) VALUES({person},{u},{number}) ON CONFLICT(person_id,unit_id) DO UPDATE SET employee_number=excluded.employee_number RETURNING {result}"))?;
    let identity = id(&identity, "id")?;
    let mut s = Statement::new(db);
    let u = s.number(unit);
    let n = s.text(employee_number);
    let empty = if s.pg { "'null'::jsonb" } else { "'null'" };
    let previous=s.query(db,read_json(format!("SELECT COALESCE((SELECT {}('id',CAST(identity_id AS TEXT)) FROM employee_number_history WHERE unit_id={u} AND employee_number={n}),{empty})",object(db.provider()=="PostgreSQL")),db.provider()=="PostgreSQL"))?;
    if !previous.is_null() && id(&previous, "id")? != identity {
        return Err(conflict("此单位工号已绑定另一人员的历史，不能复用。"));
    }
    let mut s = Statement::new(db);
    let i = s.number(identity);
    let u = s.number(unit);
    let n = s.text(employee_number);
    s.execute(db,format!("INSERT INTO employee_number_history(identity_id,unit_id,employee_number) VALUES({i},{u},{n}) ON CONFLICT(unit_id,employee_number) DO NOTHING"))?;
    Ok(identity)
}
fn children(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    unit: i64,
    parent_id: i64,
    body: &Value,
) -> Result<()> {
    let project = kind == "projects";
    let key = if project { "members" } else { "assignments" };
    let table = if project {
        "project_memberships"
    } else {
        "responsibility_assignments"
    };
    let parent_column = if project {
        "project_id"
    } else {
        "responsibility_id"
    };
    let previous =
        get(db, kind, unit, parent_id)?.ok_or_else(|| Error::unavailable("新实体未能回读。"))?;
    let entries = body[key]
        .as_array()
        .ok_or_else(|| Error::unavailable("成员列表缺失。"))?;
    for existing in previous[key].as_array().into_iter().flatten() {
        if !entries.iter().any(|entry| entry["id"] == existing["id"]) {
            return Err(conflict(
                "已有职责或项目成员须结束有效期，不能删除历史关系。",
            ));
        }
    }
    for entry in entries {
        crate::operation::check()?;
        let mut s = Statement::new(db);
        let parent = s.number(parent_id);
        let u = s.number(unit);
        let appointment = s.text(text(entry, "appointmentId"));
        let start = s.text(text(&entry["period"], "startsOn"));
        let end = s.nullable_text(entry["period"]["endsOn"].as_str());
        let mut columns = vec![
            parent_column,
            "unit_id",
            "appointment_id",
            "starts_on",
            "ends_on",
        ];
        let mut p = vec![parent, u, appointment, start, end];
        if project {
            columns.push("role");
            p.push(s.text(text(entry, "role")));
        } else {
            columns.push("project_id");
            p.push(s.nullable_number(entry["projectId"].as_str()));
        }
        if let Some(current) = entry["id"].as_str() {
            let current = s.number(current);
            let assignments = columns
                .iter()
                .zip(p)
                .map(|(c, p)| format!("{c}={p}"))
                .collect::<Vec<_>>()
                .join(",");
            let owner_column = columns[0];
            let owner = s.number(parent_id);
            if s.execute(
                db,
                format!(
                    "UPDATE {table} SET {assignments} WHERE id={current} AND {owner_column}={owner}"
                ),
            )? != 1
            {
                return Err(conflict("成员编号不属于此职责或项目。"));
            }
        } else {
            insert(db, table, &columns, p, s)?;
        }
    }
    Ok(())
}
pub(crate) fn save(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    current: Option<i64>,
    unit: i64,
    expected: i64,
    body: &Value,
) -> Result<Value> {
    let table = table(kind)?;
    let mut s = Statement::new(db);
    let (columns, p) = match kind {
        "positions" => (
            vec!["department_id", "code", "name", "is_active"],
            vec![
                s.nullable_number(body["departmentId"].as_str()),
                s.text(text(body, "code")),
                s.text(text(body, "name")),
                s.number(i64::from(body["isActive"] == true)),
            ],
        ),
        "employments" => {
            if let Some(current) = current {
                if get(db, kind, unit, current)?.is_none_or(|e| e["personId"] != body["personId"]) {
                    return Err(conflict("雇佣期间不能改绑其他人员。"));
                }
            }
            let identity = employee_identity(
                db,
                id(body, "personId")?,
                unit,
                text(body, "employeeNumber"),
            )?;
            (
                vec!["identity_id", "starts_on", "ends_on", "stage"],
                vec![
                    s.number(identity),
                    s.text(text(&body["period"], "startsOn")),
                    s.nullable_text(body["period"]["endsOn"].as_str()),
                    s.text(text(body, "stage")),
                ],
            )
        }
        "responsibilities" => (
            vec![
                "department_id",
                "code",
                "name",
                "workflow_kinds",
                "is_active",
            ],
            vec![
                s.nullable_number(body["departmentId"].as_str()),
                s.text(text(body, "code")),
                s.text(text(body, "name")),
                s.json(&body["workflowKinds"])?,
                s.number(i64::from(body["isActive"] == true)),
            ],
        ),
        "projects" => (
            vec!["code", "name", "owner_appointment_id", "is_active"],
            vec![
                s.text(text(body, "code")),
                s.text(text(body, "name")),
                s.text(text(body, "ownerAppointmentId")),
                s.number(i64::from(body["isActive"] == true)),
            ],
        ),
        _ => unreachable!(),
    };
    let saved = write(db, table, current, unit, expected, &columns, p, s)?;
    if matches!(kind, "responsibilities" | "projects") {
        let mut body = body.clone();
        body["id"] = Value::String(saved.to_string());
        children(db, kind, unit, saved, &body)?;
    }
    get(db, kind, unit, saved)?.ok_or_else(|| Error::unavailable("人员基础实体未能回读。"))
}
pub(crate) fn sync_person(db: &(impl Adapter + ?Sized), person: i64, body: &Value) -> Result<()> {
    let mut s = Statement::new(db);
    let code = s.text(text(body, "companyScope"));
    let query = read_json(
        format!(
            "SELECT {}('id',CAST(id AS TEXT)) FROM organization_units WHERE code={code}",
            object(s.pg)
        ),
        s.pg,
    );
    let unit = id(&s.query(db, query)?, "id")?;
    let identity = employee_identity(db, person, unit, text(body, "employeeNumber"))?;
    let mut s = Statement::new(db);
    let i = s.number(identity);
    let obj = object(s.pg);
    let query = read_json(
        format!(
            "SELECT {}",
            aggregate(
                &format!(
                    "SELECT {obj}('id',CAST(id AS TEXT),'startsOn',starts_on,'endsOn',ends_on,'stage',stage,'version',version) AS body FROM employments WHERE identity_id={i} ORDER BY starts_on DESC,id DESC LIMIT 1"
                ),
                s.pg
            )
        ),
        s.pg,
    );
    let rows = s.query(db, query)?;
    let previous = rows.as_array().and_then(|v| v.first());
    let date = text(body, "hireDate");
    let end = if body["status"] == "Departed" {
        let day = chrono::NaiveDate::parse_from_str(text(body, "departedOn"), "%Y-%m-%d")
            .map_err(|_| Error::unavailable("离职日期缺失。"))?;
        Some(
            day.checked_add_days(chrono::Days::new(1))
                .ok_or_else(|| Error::unavailable("离职日期溢出。"))?
                .to_string(),
        )
    } else {
        None
    };
    let stage = if end.is_some() {
        "Departed"
    } else if body["status"] == "Probation" {
        "Probation"
    } else {
        "Regular"
    };
    let current = previous.filter(|e| e["endsOn"].is_null() || text(e, "startsOn") == date);
    let version = current.and_then(|e| e["version"].as_i64()).unwrap_or(0);
    let unchanged = current.is_some_and(|e| {
        e["startsOn"] == date && e["endsOn"].as_str() == end.as_deref() && e["stage"] == stage
    });
    if !unchanged {
        let mut s = Statement::new(db);
        let p = vec![
            s.number(identity),
            s.text(date),
            s.nullable_text(end.as_deref()),
            s.text(stage),
        ];
        write(
            db,
            "employments",
            current.map(|e| id(e, "id")).transpose()?,
            unit,
            version,
            &["identity_id", "starts_on", "ends_on", "stage"],
            p,
            s,
        )?;
    }
    let mut s = Statement::new(db);
    let person = s.number(person);
    let query = read_json(
        format!(
            "SELECT {}",
            aggregate(
                &format!(
                    "SELECT {}('id',id,'unitId',CAST(unit_id AS TEXT),'departmentId',CAST(department_id AS TEXT),'title',job_title,'positionId',CAST(position_id AS TEXT)) AS body FROM personnel_memberships WHERE person_id={person}",
                    object(s.pg)
                ),
                s.pg
            )
        ),
        s.pg,
    );
    let appointments = s.query(db, query)?;
    for appointment in appointments.as_array().into_iter().flatten() {
        if text(appointment, "title").is_empty() {
            continue;
        }
        let mut s = Statement::new(db);
        let u = s.number(id(appointment, "unitId")?);
        let d = s.number(id(appointment, "departmentId")?);
        let name = s.text(text(appointment, "title"));
        let null = if s.pg { "'null'::jsonb" } else { "'null'" };
        let query = read_json(
            format!(
                "SELECT COALESCE((SELECT {}('id',CAST(id AS TEXT)) FROM positions WHERE unit_id={u} AND department_id={d} AND name={name}),{null})",
                object(s.pg)
            ),
            s.pg,
        );
        let mut position = s.query(db, query)?;
        if position.is_null() {
            let mut s = Statement::new(db);
            let u = s.number(id(appointment, "unitId")?);
            let d = s.number(id(appointment, "departmentId")?);
            let name = s.text(text(appointment, "title"));
            let code = format!(
                "AUTO-{}",
                &crate::schema::hex(
                    <sha2::Sha256 as sha2::Digest>::digest(serde_json::to_vec(&[
                        appointment["unitId"].clone(),
                        appointment["departmentId"].clone(),
                        appointment["title"].clone()
                    ])?)
                    .as_ref()
                )[..24]
            );
            let code = s.text(code);
            position = insert(
                db,
                "positions",
                &[
                    "unit_id",
                    "department_id",
                    "code",
                    "name",
                    "is_active",
                    "version",
                ],
                vec![u, d, code, name, "1".into(), "1".into()],
                s,
            )
            .map(|id| serde_json::json!({"id":id.to_string()}))?;
        }
        let mut s = Statement::new(db);
        let p = s.number(id(&position, "id")?);
        let a = s.text(text(appointment, "id"));
        s.execute(
            db,
            format!("UPDATE personnel_memberships SET position_id={p} WHERE id={a}"),
        )?;
    }
    Ok(())
}
