//! Unit-scoped service identities and memberships. Callers own authorization and transactions.
use crate::{Adapter, Error, ErrorKind, Result, sql};
use serde_json::Value;

pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    company: &str,
    category: &str,
    after: &str,
    limit: i64,
) -> Result<Vec<Value>> {
    search(db, company, category, "", "", "", after, limit)
}

pub(crate) fn search(
    db: &(impl Adapter + ?Sized),
    company: &str,
    category: &str,
    resource: &str,
    keyword: &str,
    state: &str,
    after: &str,
    limit: i64,
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let after = sql::bind(&mut values, after, pg);
    let mut filter = format!("company={company} AND service_id>{after}");
    if !category.is_empty() {
        filter += &format!(" AND category={}", sql::bind(&mut values, category, pg));
    }
    match resource {
        "office.general" => {
            filter += " AND category IN ('Seal','Certificate','IT','Repair','Other')"
        }
        "office.supplies" => filter += " AND category='Supply'",
        "office.rooms" => filter += " AND category='Room'",
        _ => (),
    }
    match state {
        "active" => filter += " AND enabled=1",
        "inactive" => filter += " AND enabled=0",
        _ => (),
    }
    if !keyword.is_empty() {
        let keyword = sql::bind(&mut values, keyword.to_lowercase(), pg);
        filter += &if pg {
            format!(" AND strpos(lower(body->>'name'),{keyword})>0")
        } else {
            format!(" AND instr(lower(json_extract(body,'$.name')),{keyword})>0")
        };
    }
    let limit = sql::bind(&mut values, limit.clamp(1, 100), pg);
    let select = format!(
        "SELECT body,revision FROM handling_services WHERE {filter} ORDER BY service_id LIMIT CAST({limit} AS BIGINT)"
    );
    let expression = if pg {
        "body || jsonb_build_object('revision',revision)"
    } else {
        "json_set(body,'$.revision',revision)"
    };
    let query = if pg {
        format!(
            "SELECT COALESCE(jsonb_agg({expression}),'[]'::jsonb)::text FROM ({select}) selected"
        )
    } else {
        format!("SELECT json_group_array(json({expression})) FROM ({select}) selected")
    };
    serde_json::from_value(db.query_json(&query, &values)?).map_err(Into::into)
}

pub(crate) fn get(db: &(impl Adapter + ?Sized), company: &str, id: &str) -> Result<Option<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let id = sql::bind(&mut values, id, pg);
    let projection = if pg {
        "body || jsonb_build_object('revision',revision)"
    } else {
        "json_set(body,'$.revision',revision)"
    };
    let value = db.query_json(&format!("SELECT CAST(COALESCE((SELECT {projection} FROM handling_services WHERE company={company} AND service_id={id}), 'null') AS TEXT)"), &values)?;
    Ok((!value.is_null()).then_some(value))
}

pub(crate) fn save(db: &(impl Adapter + ?Sized), company: &str, service: &Value) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company_arg = sql::bind(&mut values, company, pg);
    let id = sql::bind(
        &mut values,
        service["key"]
            .as_str()
            .ok_or_else(|| Error::unavailable("办理事项编号缺失。"))?,
        pg,
    );
    let projection = if pg {
        "body || jsonb_build_object('revision',revision)"
    } else {
        "json_set(body,'$.revision',revision)"
    };
    let current = db.query_json(&format!("SELECT CAST(COALESCE((SELECT {projection} FROM handling_services WHERE company={company_arg} AND service_id={id}), '{}') AS TEXT)", "{}"), &values)?;
    let revision = current["revision"].as_i64().unwrap_or(0);
    if service["revision"].as_i64().unwrap_or(0) != revision {
        return Err(Error::new(
            ErrorKind::Conflict,
            "办理事项已被修改，请重新读取后核对。",
        ));
    }
    let mut body = service.clone();
    for key in ["revision", "handlerNames"] {
        body.as_object_mut().unwrap().remove(key);
    }
    let mut previous = current;
    if let Some(previous) = previous.as_object_mut() {
        previous.remove("revision");
        previous.remove("handlerNames");
    }
    if revision > 0 && previous == body {
        return Ok(());
    }
    let body = sql::bind(&mut values, body.to_string(), pg);
    let category = sql::bind(&mut values, service["category"].as_str().unwrap_or(""), pg);
    let enabled = sql::bind(&mut values, i32::from(service["isActive"] == true), pg);
    let name = sql::bind(
        &mut values,
        service["name"].as_str().unwrap_or("").trim().to_lowercase(),
        pg,
    );
    let next = sql::bind(&mut values, revision + 1, pg);
    let body = if pg {
        format!("CAST({body} AS JSONB)")
    } else {
        body
    };
    let changed = db.execute_statement(&format!("INSERT INTO handling_services(company,service_id,category,name_key,enabled,revision,body) VALUES({company_arg},{id},{category},{name},CAST({enabled} AS INTEGER),CAST({next} AS BIGINT),{body}) ON CONFLICT(company,service_id) DO UPDATE SET category=excluded.category,name_key=excluded.name_key,enabled=excluded.enabled,revision=excluded.revision,body=excluded.body WHERE handling_services.revision=excluded.revision-1"), &values)?;
    if changed != 1 {
        return Err(Error::new(
            ErrorKind::Conflict,
            "办理事项已被修改，请重新读取后核对。",
        ));
    }
    db.execute_statement(
        &format!(
            "DELETE FROM handling_service_assignees WHERE company={company_arg} AND service_id={id}"
        ),
        &values[..2],
    )?;
    for user in service["handlerUserIds"].as_array().into_iter().flatten() {
        let mut args = values[..2].to_vec();
        let user = sql::bind(
            &mut args,
            user.as_i64()
                .ok_or_else(|| Error::unavailable("办理账号编号无效。"))?,
            pg,
        );
        db.execute_statement(&format!("INSERT INTO handling_service_assignees(company,service_id,user_id) VALUES({company_arg},{id},CAST({user} AS BIGINT))"), &args)?;
    }
    Ok(())
}

pub(crate) fn remove(db: &(impl Adapter + ?Sized), company: &str, id: &str) -> Result<()> {
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let company = sql::bind(&mut values, company, pg);
    let id = sql::bind(&mut values, id, pg);
    db.execute_statement(
        &format!("DELETE FROM handling_services WHERE company={company} AND service_id={id}"),
        &values,
    )?;
    Ok(())
}
