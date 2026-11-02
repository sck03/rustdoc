//! Project options expose only names for an effective applicant membership.
use super::*;

pub(crate) fn options(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    appointment: &str,
    day: &str,
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let mut statement = Statement::new(db);
    let unit = statement.number(unit);
    let appointment = statement.text(appointment);
    let day = statement.text(day);
    let filter = format!(
        "r.unit_id={unit} AND r.is_active=1 AND (r.owner_appointment_id={appointment} OR EXISTS(SELECT 1 FROM project_memberships member WHERE member.project_id=r.id AND member.appointment_id={appointment} AND member.starts_on<={day} AND (member.ends_on IS NULL OR {day}<member.ends_on)))"
    );
    let body = format!(
        "{}('id',CAST(r.id AS TEXT),'unitId',CAST(r.unit_id AS TEXT),'code',r.code,'name',r.name)",
        object(statement.pg)
    );
    let items = json_value(
        &aggregate(
            &format!(
                "SELECT {body} AS body FROM project_scopes r WHERE {filter} ORDER BY r.id DESC LIMIT {} OFFSET {}",
                limit.clamp(1, 100),
                offset.max(0)
            ),
            statement.pg,
        ),
        statement.pg,
    );
    let query = read_json(
        format!(
            "SELECT {}('items',{items},'totalCount',(SELECT COUNT(*) FROM project_scopes r WHERE {filter}))",
            object(statement.pg)
        ),
        statement.pg,
    );
    statement.query(db, query)
}
