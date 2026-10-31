//! Indexed public account links and appointment references shared by both adapters.
use crate::{QuerySql, Result, sql};
pub(crate) fn unit_accounts(
    company: &str,
    date: &str,
    all: bool,
    pg: bool,
) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let mut filter = if pg {
        "r.kind='users' AND (r.body->>'isActive')::boolean AND r.body->>'role'<>'Admin'".to_owned()
    } else {
        "r.kind='users' AND json_extract(r.body,'$.isActive')=1 AND json_extract(r.body,'$.role')<>'Admin'".to_owned()
    };
    if !all {
        let unit = sql::bind(&mut values, company, pg);
        let date = sql::bind(&mut values, date, pg);
        filter += &format!(
            " AND (r.company={unit} OR EXISTS(SELECT 1 FROM personnel_accounts a JOIN personnel_memberships m ON m.person_id=a.person_id WHERE a.user_id=r.id AND m.company={unit} AND (m.starts_on='' OR m.starts_on<={date}) AND (m.ends_on='' OR m.ends_on>={date})))"
        );
    }
    Ok((
        QuerySql {
            filter,
            order: "r.id DESC".into(),
            values,
        },
        sql::projection(
            &[
                ("id", "id"),
                ("fullName", "fullName"),
                ("username", "username"),
                ("companyScope", "companyScope"),
                ("role", "role"),
            ],
            pg,
        )?,
    ))
}

fn parameters(ids: &[i64], pg: bool) -> (Vec<String>, String) {
    let mut values = vec![];
    let ids = ids
        .iter()
        .map(|id| format!("CAST({} AS BIGINT)", sql::bind(&mut values, id, pg)))
        .collect::<Vec<_>>()
        .join(",");
    (values, ids)
}

pub(crate) fn accounts(
    ids: &[i64],
    fields: &[(&str, &str)],
    pg: bool,
) -> Result<(QuerySql, String)> {
    let (values, ids) = parameters(ids, pg);
    let query = QuerySql {
        filter: format!(
            "r.kind='people' AND r.id IN (SELECT a.person_id FROM personnel_accounts a WHERE a.user_id IN ({ids}))"
        ),
        order: "r.id DESC".into(),
        values,
    };
    Ok((query, sql::projection(fields, pg)?))
}

pub(crate) fn appointments(ids: &[i64], pg: bool) -> Result<(QuerySql, String)> {
    let role = "(SELECT COUNT(*) FROM unit_access_roles a WHERE a.appointment_id=m.id)";
    let reporting =
        "(SELECT COUNT(*) FROM reporting_relations a WHERE a.manager_appointment_id=m.id)";
    let duty = "(SELECT COUNT(*) FROM organization_duties a WHERE a.appointment_id=m.id)";
    let history = if pg {
        "(SELECT COUNT(*) FROM record_rows a WHERE a.kind='oa-submission' AND a.body #>> '{appointment,id}'=m.id)"
    } else {
        "(SELECT COUNT(*) FROM record_rows a WHERE a.kind='oa-submission' AND json_extract(a.body,'$.appointment.id')=m.id)"
    };
    let items = if pg {
        format!(
            "(SELECT COALESCE(jsonb_agg(jsonb_build_object('id',m.id,'roleCount',{role},'reportingCount',{reporting},'dutyCount',{duty},'historyCount',{history})), '[]'::jsonb) FROM personnel_memberships m WHERE m.person_id=r.id)"
        )
    } else {
        format!(
            "(SELECT json_group_array(json_object('id',m.id,'roleCount',{role},'reportingCount',{reporting},'dutyCount',{duty},'historyCount',{history})) FROM personnel_memberships m WHERE m.person_id=r.id)"
        )
    };
    let (values, ids) = parameters(ids, pg);
    let query = QuerySql {
        filter: format!("r.kind='people' AND r.id IN ({ids})"),
        order: "r.id DESC".into(),
        values,
    };
    Ok((
        query,
        sql::projected(
            [
                Ok(("id", sql::json_field("id", pg))),
                Ok(("appointments", items)),
            ],
            pg,
        )?,
    ))
}
