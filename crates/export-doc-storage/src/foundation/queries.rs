use super::*;
fn projection(kind: &str, pg: bool) -> Result<(String, String)> {
    let obj = object(pg);
    let active = boolean("r.is_active", pg);
    let children = |table: &str, parent: &str, project: bool| {
        let extra = if project {
            "'role',m.role"
        } else {
            "'projectId',CAST(m.project_id AS TEXT),'role',''"
        };
        let period = json_value(
            &format!("{obj}('startsOn',m.starts_on,'endsOn',m.ends_on)"),
            pg,
        );
        let rows = aggregate(
            &format!(
                "SELECT {obj}('id',CAST(m.id AS TEXT),'appointmentId',m.appointment_id,{extra},'period',{period}) AS body FROM {table} m WHERE m.{parent}=r.id ORDER BY m.starts_on,m.id"
            ),
            pg,
        );
        json_value(&rows, pg)
    };
    let (properties, joins) = match kind {
        "positions" => (
            format!(
                "'departmentId',CAST(r.department_id AS TEXT),'code',r.code,'name',r.name,'isActive',{active},'appointmentCount',(SELECT COUNT(*) FROM personnel_memberships m WHERE m.position_id=r.id)"
            ),
            String::new(),
        ),
        "employments" => {
            let period = json_value(
                &format!("{obj}('startsOn',r.starts_on,'endsOn',r.ends_on)"),
                pg,
            );
            (
                format!(
                    "'identityId',CAST(e.id AS TEXT),'personId',CAST(e.person_id AS TEXT),'employeeNumber',e.employee_number,'stage',r.stage,'period',{period}"
                ),
                "JOIN employee_identities e ON e.id=r.identity_id".into(),
            )
        }
        "responsibilities" => (
            format!(
                "'departmentId',CAST(r.department_id AS TEXT),'code',r.code,'name',r.name,'workflowKinds',{},'assignments',{},'isActive',{active}",
                json_value("r.workflow_kinds", pg),
                children("responsibility_assignments", "responsibility_id", false)
            ),
            String::new(),
        ),
        "projects" => (
            format!(
                "'code',r.code,'name',r.name,'ownerAppointmentId',r.owner_appointment_id,'members',{},'isActive',{active}",
                children("project_memberships", "project_id", true)
            ),
            String::new(),
        ),
        _ => return Err(Error::unavailable("未知人员基础实体。")),
    };
    Ok((
        format!(
            "{obj}('id',CAST(r.id AS TEXT),'unitId',CAST(r.unit_id AS TEXT),'versionNumber',r.version,{properties})"
        ),
        joins,
    ))
}
pub(crate) fn list(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    unit: i64,
    scope: &crate::DataScope<'_>,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let table = table(kind)?;
    let mut s = Statement::new(db);
    let (body, joins) = projection(kind, s.pg)?;
    let unit = s.number(unit);
    let offset = offset.max(0);
    let limit = limit.clamp(1, 100);
    let mut filter = format!("r.unit_id={unit}");
    if kind == "employments" {
        let permitted = scope.filter(&mut s.values, s.pg).replace("r.", "p.");
        filter += &format!(
            " AND EXISTS(SELECT 1 FROM employee_identities i JOIN records p ON p.id=i.person_id WHERE i.id=r.identity_id AND p.kind='people' AND {permitted})"
        );
    }
    let items = json_value(
        &aggregate(
            &format!(
                "SELECT {body} AS body FROM {table} r {joins} WHERE {filter} ORDER BY r.id DESC LIMIT {limit} OFFSET {offset}"
            ),
            s.pg,
        ),
        s.pg,
    );
    let query = read_json(
        format!(
            "SELECT {}('items',{items},'totalCount',(SELECT COUNT(*) FROM {table} r WHERE {filter}),'pageNumber',{},'pageSize',{limit})",
            object(s.pg),
            offset / limit + 1
        ),
        s.pg,
    );
    s.query(db, query)
}
pub(crate) fn catalog(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    unit: i64,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let table = table(kind)?;
    let mut s = Statement::new(db);
    let unit = s.number(unit);
    let body = format!(
        "{}('id',CAST(r.id AS TEXT),'unitId',CAST(r.unit_id AS TEXT),'code',r.code,'name',r.name)",
        object(s.pg)
    );
    let filter = format!("r.unit_id={unit} AND r.is_active=1");
    let items = json_value(
        &aggregate(
            &format!(
                "SELECT {body} AS body FROM {table} r WHERE {filter} ORDER BY r.id DESC LIMIT {} OFFSET {}",
                limit.clamp(1, 100),
                offset.max(0)
            ),
            s.pg,
        ),
        s.pg,
    );
    let query = read_json(
        format!(
            "SELECT {}('items',{items},'totalCount',(SELECT COUNT(*) FROM {table} r WHERE {filter}))",
            object(s.pg)
        ),
        s.pg,
    );
    s.query(db, query)
}
pub(crate) fn get(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    unit: i64,
    id: i64,
) -> Result<Option<Value>> {
    let table = table(kind)?;
    let mut s = Statement::new(db);
    let (body, joins) = projection(kind, s.pg)?;
    let unit = s.number(unit);
    let id = s.number(id);
    let null = if s.pg { "'null'::jsonb" } else { "'null'" };
    let query = read_json(
        format!(
            "SELECT COALESCE((SELECT {body} FROM {table} r {joins} WHERE r.unit_id={unit} AND r.id={id}),{null})"
        ),
        s.pg,
    );
    let value = s.query(db, query)?;
    Ok((!value.is_null()).then_some(value))
}
pub(crate) fn resolve_duty(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    responsibility: i64,
    project: Option<i64>,
    kind: &str,
    date: &str,
) -> Result<Vec<Value>> {
    let mut s = Statement::new(db);
    let unit = s.number(unit);
    let responsibility = s.number(responsibility);
    let date = s.text(date);
    let project = s.optional_number(project.as_ref().map(|id| id.to_string()).as_deref());
    let kind = s.text(kind);
    let eligible = authority::responsibility(s.pg, ("r", "a", "m"), &project, &kind, &date);
    let obj = object(s.pg);
    let select = format!(
        "SELECT {obj}('personId',m.person_id,'appointmentId',m.id,'assignmentId',CAST(a.id AS TEXT)) AS body FROM responsibilities r JOIN responsibility_assignments a ON a.responsibility_id=r.id JOIN personnel_memberships m ON m.id=a.appointment_id WHERE r.id={responsibility} AND r.unit_id={unit} AND {eligible} ORDER BY m.person_id,m.id"
    );
    let query = read_json(format!("SELECT {}", aggregate(&select, s.pg)), s.pg);
    Ok(serde_json::from_value(s.query(db, query)?)?)
}
pub(crate) fn appointment(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    key: &str,
) -> Result<Option<Value>> {
    let mut s = Statement::new(db);
    let u = s.number(unit);
    let key = s.text(key);
    let null = if s.pg { "'null'::jsonb" } else { "'null'" };
    let query = read_json(
        format!(
            "SELECT COALESCE((SELECT {}('personId',person_id,'departmentId',CAST(department_id AS TEXT),'positionId',CAST(position_id AS TEXT)) FROM personnel_memberships WHERE id={key} AND unit_id={u}),{null})",
            object(s.pg)
        ),
        s.pg,
    );
    let value = s.query(db, query)?;
    Ok((!value.is_null()).then_some(value))
}
pub(crate) fn placement_appointments(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    source: &str,
    id: i64,
    date: &str,
) -> Result<Vec<Value>> {
    let mut s = Statement::new(db);
    let u = s.number(unit);
    let id = s.number(id);
    let date = s.text(date);
    let selected = authority::placement(source, "m", &u, &id, &date)?;
    let current = authority::appointment("m", &date);
    let obj = object(s.pg);
    let select = format!(
        "SELECT {obj}('personId',m.person_id,'appointmentId',m.id,'userId',a.user_id) AS body FROM personnel_memberships m JOIN persons p ON p.id=m.person_id LEFT JOIN personnel_accounts a ON a.person_id=m.person_id WHERE m.unit_id={u} AND p.status IN ('Active','Probation') AND {current} AND ({selected}) ORDER BY m.person_id,m.id LIMIT 201"
    );
    let query = read_json(format!("SELECT {}", aggregate(&select, s.pg)), s.pg);
    let values: Vec<Value> = serde_json::from_value(s.query(db, query)?)?;
    if values.len() > 200 {
        return Err(Error::new(
            ErrorKind::Busy,
            "岗位或项目责任候选超过二百人，请缩小审批职责范围。",
        ));
    }
    Ok(values)
}
