//! Shared list/export selection applies scope and field searches before allocating rows.
use crate::{Adapter, DataScope, GenericQuery, Result, generic_query, sql};
use serde_json::Value;
/// Bound and deduplicated lookups; callers validate business scope in the same snapshot.
pub(crate) fn by_keys(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    numeric: bool,
    keys: &[String],
    fields: &[(&str, &str)],
) -> Result<Vec<Value>> {
    let pg = db.provider() == "PostgreSQL";
    let mut keys: Vec<_> = keys
        .iter()
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect();
    if numeric {
        keys.sort_by_key(|key| std::cmp::Reverse(key.parse::<i64>().expect("numeric record id")));
    }
    let mut result = vec![];
    let projection = sql::projection(fields, pg)?;
    for chunk in keys.chunks(200) {
        crate::operation::check()?;
        let mut values = vec![];
        let bound_kind = sql::bind(&mut values, kind, pg);
        let bound = chunk
            .iter()
            .map(|key| {
                let value = sql::bind(&mut values, key, pg);
                if numeric {
                    format!("CAST({value} AS BIGINT)")
                } else {
                    value
                }
            })
            .collect::<Vec<_>>()
            .join(",");
        let column = if numeric { "id" } else { "identity" };
        let query = crate::QuerySql {
            source: crate::query_source::Source::for_kind(kind),
            filter: format!("r.kind={bound_kind} AND r.{column} IN ({bound})"),
            order: "r.id DESC".into(),
            values,
        };
        result.extend(db.query_slice(&query, &projection, 0, 200)?);
    }
    Ok(result)
}
pub(crate) fn page(
    db: &(impl Adapter + ?Sized),
    query: &GenericQuery<'_>,
    search_fields: &[&str],
    count: bool,
) -> Result<(i64, Vec<Value>)> {
    selected_page(db, query, search_fields, count, None)
}
pub(crate) fn ordered_page(
    db: &(impl Adapter + ?Sized),
    query: &GenericQuery<'_>,
    search_fields: &[&str],
    order: crate::RecordOrder,
) -> Result<(i64, Vec<Value>)> {
    selected_page(db, query, search_fields, true, Some(order))
}
fn selected_page(
    db: &(impl Adapter + ?Sized),
    query: &GenericQuery<'_>,
    search_fields: &[&str],
    count: bool,
    order: Option<crate::RecordOrder>,
) -> Result<(i64, Vec<Value>)> {
    let pg = db.provider() == "PostgreSQL";
    let selected = GenericQuery {
        kind: query.kind,
        scope: query.scope.clone(),
        filters: query.filters,
        search_root: query.search_root,
        fields: query.fields,
        offset: query.offset,
        limit: query.limit,
        ..Default::default()
    };
    let (mut statement, projection) = generic_query::build(&selected, pg)?;
    if let Some(order) = order {
        statement.order = crate::ordered_query::expression(order, pg);
    }
    if !query.keyword.is_empty() {
        let mut choices = vec![];
        let keyword = sql::bind(&mut statement.values, query.keyword, pg);
        for field in search_fields {
            sql::valid_path(field)?;
            choices.push(sql::contains(&sql::field(field, pg), &keyword, pg));
        }
        statement.filter += &format!(
            " AND ({})",
            if choices.is_empty() {
                "FALSE".into()
            } else {
                choices.join(" OR ")
            }
        );
    }
    if count {
        db.query_page(&statement, &projection, query.offset, query.limit)
    } else {
        Ok((
            0,
            db.query_slice(&statement, &projection, query.offset, query.limit)?,
        ))
    }
}
pub(crate) fn contacts(
    db: &(impl Adapter + ?Sized),
    kind: &str,
    scope: &DataScope<'_>,
    relation: &str,
    parents: &[i64],
) -> Result<Vec<Value>> {
    if parents.is_empty() {
        return Ok(vec![]);
    }
    sql::valid_path(relation)?;
    let pg = db.provider() == "PostgreSQL";
    let mut values = vec![];
    let kind = sql::bind(&mut values, kind, pg);
    let allowed = scope.filter(&mut values, pg);
    let ids = parents
        .iter()
        .map(|id| format!("CAST({} AS BIGINT)", sql::bind(&mut values, id, pg)))
        .collect::<Vec<_>>()
        .join(",");
    let parent = sql::field(relation, pg);
    let primary = sql::field("isPrimary", pg);
    let first = if pg {
        format!("COALESCE(({primary})::boolean,false)")
    } else {
        format!("COALESCE({primary},0)=1")
    };
    let projection = sql::projection(
        &[
            ("id", "id"),
            ("parentId", relation),
            ("name", "name"),
            ("title", "title"),
            ("email", "email"),
            ("phone", "phone"),
            ("instantMessaging", "instantMessaging"),
        ],
        pg,
    )?;
    let select = format!(
        "SELECT {projection} AS body,ROW_NUMBER() OVER(PARTITION BY {parent} ORDER BY CASE WHEN {first} THEN 0 ELSE 1 END,r.id) AS selected FROM record_rows r WHERE r.kind={kind} AND ({allowed}) AND CAST({parent} AS BIGINT) IN ({ids})"
    );
    let aggregate = if pg {
        "COALESCE(jsonb_agg(body::jsonb),'[]'::jsonb)::text"
    } else {
        "json_group_array(json(body))"
    };
    Ok(serde_json::from_value(db.query_json(
        &format!("SELECT {aggregate} FROM ({select}) candidates WHERE selected=1"),
        &values,
    )?)?)
}
