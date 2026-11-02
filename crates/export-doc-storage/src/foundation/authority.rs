//! Shared effective-role predicates for resolution and current workflow reads.
pub(crate) fn appointment(m: &str, date: &str) -> String {
    format!(
        "({m}.is_primary=0 OR {m}.ends_on='') AND ({m}.starts_on='' OR {m}.starts_on<={date}) AND ({m}.ends_on='' OR {m}.ends_on>={date})"
    )
}
pub(crate) fn contains(pg: bool, values: &str, value: &str) -> String {
    if pg {
        format!("({values} ? {value})")
    } else {
        format!("EXISTS(SELECT 1 FROM json_each({values}) WHERE value={value})")
    }
}
pub(crate) fn placement(
    source: &str,
    m: &str,
    unit: &str,
    id: &str,
    date: &str,
) -> crate::Result<String> {
    Ok(match source {
        "Position" => format!(
            "{m}.position_id={id} AND EXISTS(SELECT 1 FROM positions p WHERE p.id={id} AND p.unit_id={unit} AND p.is_active=1)"
        ),
        "ProjectLeader" => format!(
            "EXISTS(SELECT 1 FROM project_scopes p WHERE p.id={id} AND p.unit_id={unit} AND p.is_active=1 AND (p.owner_appointment_id={m}.id OR EXISTS(SELECT 1 FROM project_memberships x WHERE x.project_id=p.id AND x.appointment_id={m}.id AND x.role='Leader' AND x.starts_on<={date} AND (x.ends_on IS NULL OR {date}<x.ends_on))))"
        ),
        _ => return Err(crate::Error::unavailable("岗位或项目来源无效。")),
    })
}
pub(crate) fn responsibility(
    pg: bool,
    aliases: (&str, &str, &str),
    project: &str,
    kind: &str,
    date: &str,
) -> String {
    let (r, a, m) = aliases;
    let kinds = contains(pg, &format!("{r}.workflow_kinds"), kind);
    format!(
        "{r}.is_active=1 AND {kinds} AND {a}.starts_on<={date} AND ({a}.ends_on IS NULL OR {date}<{a}.ends_on) AND ({a}.project_id IS NULL OR {a}.project_id={project} AND EXISTS(SELECT 1 FROM project_scopes p JOIN project_memberships x ON x.project_id=p.id WHERE p.id={project} AND p.is_active=1 AND x.appointment_id={m}.id AND x.starts_on<={date} AND (x.ends_on IS NULL OR {date}<x.ends_on)))"
    )
}
