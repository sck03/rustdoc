//! Public candidates never read private personnel records or profiles.
use super::*;
pub(crate) fn list(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    keyword: &str,
    date: &str,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let mut s = Statement::new(db);
    let unit = s.number(unit);
    let date = s.text(date);
    let term = s.text(keyword);
    let obj = object(s.pg);
    let text = sql::contains("p.full_name", &term, s.pg);
    let number = sql::contains("COALESCE(e.employee_number,p.employee_number)", &term, s.pg);
    let active = format!(
        "m.unit_id={unit} AND (m.starts_on='' OR m.starts_on<={date}) AND (m.ends_on='' OR m.ends_on>={date})"
    );
    let filter = format!(
        "p.status IN ('Active','Probation') AND EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.person_id=p.id AND {active}) AND ({term}='' OR {text} OR {number})"
    );
    let appointments = json_value(
        &aggregate(
            &format!(
                "SELECT {obj}('id',m.id,'departmentId',CAST(m.department_id AS TEXT),'departmentCode',m.department,'jobTitle',m.job_title,'positionId',CAST(m.position_id AS TEXT),'isPrimary',{}) AS body FROM personnel_memberships m WHERE m.person_id=p.id AND {active} ORDER BY m.is_primary DESC,m.id",
                boolean("m.is_primary", s.pg)
            ),
            s.pg,
        ),
        s.pg,
    );
    let select = format!(
        "SELECT {obj}('personId',CAST(p.id AS TEXT),'name',p.full_name,'employeeNumber',COALESCE(e.employee_number,p.employee_number),'appointments',{appointments}) AS body FROM persons p LEFT JOIN employee_identities e ON e.person_id=p.id AND e.unit_id={unit} WHERE {filter} ORDER BY p.full_name,p.id LIMIT {} OFFSET {}",
        limit.clamp(1, 100),
        offset.max(0)
    );
    let items = json_value(&aggregate(&select, s.pg), s.pg);
    let departments = json_value(
        &aggregate(
            &format!(
                "SELECT {obj}('id',CAST(d.id AS TEXT),'code',d.code,'name',l.name) AS body FROM organization_departments d JOIN organization_labels l ON l.id=d.id WHERE d.unit_id={unit} AND l.is_active=1 ORDER BY d.code,d.id LIMIT 500"
            ),
            s.pg,
        ),
        s.pg,
    );
    let query = read_json(
        format!(
            "SELECT {obj}('items',{items},'totalCount',(SELECT COUNT(*) FROM persons p LEFT JOIN employee_identities e ON e.person_id=p.id AND e.unit_id={unit} WHERE {filter}),'pageNumber',{},'pageSize',{},'departments',{departments},'departmentCount',(SELECT COUNT(*) FROM organization_departments d JOIN organization_labels l ON l.id=d.id WHERE d.unit_id={unit} AND l.is_active=1))",
            offset.max(0) / limit.clamp(1, 100) + 1,
            limit.clamp(1, 100)
        ),
        s.pg,
    );
    s.query(db, query)
}
