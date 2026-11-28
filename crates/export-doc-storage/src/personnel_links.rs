//! Indexed public account links and appointment references shared by both adapters.
use crate::{Adapter, QuerySql, Result, Value, sql};

pub(crate) fn reporting_reference_count(
    db: &(impl Adapter + ?Sized),
    company: &str,
    employee: i64,
    active_on: Option<&str>,
) -> Result<usize> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let employee = sql::bind(&mut values, employee, pg);
    // Ended primary appointments are history, not current assignments. Multiple
    // reporting lines and appointments still count each reporting person once.
    let mut filter = format!(
        "manager.person_id=CAST({employee} AS BIGINT) AND member.company={company} AND (member.is_primary=0 OR member.ends_on='')"
    );
    if let Some(date) = active_on {
        let date = sql::bind(&mut values, date, pg);
        filter += &format!(
            " AND person.status IN ('Active','Probation') AND (member.starts_on='' OR member.starts_on<={date}) AND (member.ends_on='' OR member.ends_on>={date})"
        );
    }
    let value = db.query_json(&format!("SELECT CAST(COUNT(DISTINCT member.person_id) AS TEXT) FROM personnel_memberships manager JOIN reporting_relations relation ON relation.manager_appointment_id=manager.id JOIN personnel_memberships member ON member.id=relation.appointment_id JOIN persons person ON person.id=member.person_id WHERE {filter}"), &values)?;
    value
        .as_u64()
        .and_then(|count| usize::try_from(count).ok())
        .ok_or_else(|| crate::Error::unavailable("汇报关系计数无效。"))
}
pub(crate) fn unit_accounts(
    company: &str,
    date: &str,
    all: bool,
    include_admin: bool,
    pg: bool,
) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let mut filter = if pg {
        "r.kind='users' AND (r.body->>'isActive')::boolean".to_owned()
    } else {
        "r.kind='users' AND json_extract(r.body,'$.isActive')=1".to_owned()
    };
    if !include_admin {
        filter += if pg {
            " AND r.body->>'role'<>'Admin'"
        } else {
            " AND json_extract(r.body,'$.role')<>'Admin'"
        };
    }
    if !all {
        let unit = sql::bind(&mut values, company, pg);
        let date = sql::bind(&mut values, date, pg);
        let descendants = if pg {
            "ar.body->>'includeDescendants'='true'"
        } else {
            "json_extract(ar.body,'$.includeDescendants')=1"
        };
        let ancestry = format!(
            "WITH RECURSIVE lineage(id,parent_id,depth) AS (SELECT id,parent_id,1 FROM organization_units WHERE code={unit} UNION ALL SELECT p.id,p.parent_id,l.depth+1 FROM organization_units p JOIN lineage l ON p.id=l.parent_id WHERE l.depth<32) SELECT id FROM lineage"
        );
        filter += &format!(
            " AND (r.company={unit} OR EXISTS(SELECT 1 FROM personnel_accounts a JOIN personnel_memberships m ON m.person_id=a.person_id WHERE a.user_id=r.id AND m.company={unit} AND (m.starts_on='' OR m.starts_on<={date}) AND (m.ends_on='' OR m.ends_on>={date})) OR EXISTS(SELECT 1 FROM unit_access_roles ur JOIN records ar ON ar.id=ur.id JOIN organization_units u ON u.id=ur.unit_id WHERE ur.user_id=r.id AND ur.is_active=1 AND ur.mode<>'Administration' AND (u.code={unit} OR ({descendants} AND ur.unit_id IN ({ancestry})))))"
        );
    }
    Ok((
        QuerySql {
            source: crate::query_source::Source::Projected,
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

pub(crate) fn account_page(
    db: &(impl Adapter + ?Sized),
    company: &str,
    date: &str,
    all: bool,
    include_admin: bool,
    before: Option<i64>,
    limit: i64,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let (mut query, projection) = unit_accounts(company, date, all, include_admin, pg)?;
    if let Some(id) = before {
        let parameter = sql::bind(&mut query.values, id, pg);
        query.filter += &format!(" AND r.id<CAST({parameter} AS BIGINT)");
    }
    db.query_slice(&query, &projection, 0, limit.clamp(1, 200))
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
        source: crate::query_source::Source::Projected,
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
        "(SELECT COUNT(*) FROM request_revisions a WHERE a.body #>> '{appointment,id}'=m.id)"
    } else {
        "(SELECT COUNT(*) FROM request_revisions a WHERE json_extract(a.body,'$.appointment.id')=m.id)"
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
        source: crate::query_source::Source::Projected,
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
