//! Public appointment projection; private profile fields use a separate table.
use crate::sql;

pub(crate) fn filter(
    values: &mut Vec<String>,
    pg: bool,
    company: &str,
    departments: Option<&str>,
    date: Option<&str>,
) -> String {
    let mut clauses = vec![
        "m.person_id=r.id".to_owned(),
        format!("m.company={company}"),
    ];
    if let Some(departments) = departments {
        clauses.push(format!("m.department IN ({departments})"));
    }
    if let Some(date) = date {
        let date = sql::bind(values, date, pg);
        clauses.push(format!(
            "(m.starts_on='' OR m.starts_on<={date}) AND (m.ends_on='' OR m.ends_on>={date})"
        ));
    }
    format!(
        "EXISTS(SELECT 1 FROM personnel_memberships m WHERE {})",
        clauses.join(" AND ")
    )
}
