//! Bounded HS reads reuse provider ordering and keep the shared semantic matcher.
use super::{error::Result, hs, records::text, store};
use export_doc_domain::hs as rules;
use export_doc_storage::{Connection, HsQuery};
use serde_json::{Value, json};

pub(super) fn visit(
    tx: &Connection,
    query: &mut HsQuery<'_>,
    mut visit: impl FnMut(Value) -> Result<()>,
) -> Result<()> {
    loop {
        crate::operation::check()?;
        let rows = tx.query_hs(query)?;
        let length = rows.len() as i64;
        for row in rows {
            visit(row)?;
        }
        query.offset += length;
        if length < query.limit {
            return Ok(());
        }
    }
}

pub(super) fn query<'a>(kind: &'a str, fields: &'a [(&'a str, &'a str)]) -> HsQuery<'a> {
    HsQuery {
        kind,
        fields,
        current_year: None,
        prefix: "",
        ranked_examples: false,
        offset: 0,
        limit: 200,
    }
}

pub(super) fn page(
    tx: &Connection,
    kind: &str,
    parameters: &[(&str, String)],
    year: i32,
) -> Result<Value> {
    let filter = rules::text(hs::read(parameters, "keyword"));
    let (page_number, page_size, offset) = store::page_parameters(parameters);
    let mut count = 0_i64;
    let mut items = Vec::with_capacity(page_size);
    visit(tx, &mut query(kind, &[]), |mut row| {
        let fields = if kind == hs::CODES {
            &["code", "name", "elements", "description"][..]
        } else {
            &[
                "rawReportedHsCode",
                "resolvedCurrentHsCode",
                "productName",
                "specification",
            ][..]
        };
        if filter.is_empty()
            || fields
                .iter()
                .any(|key| rules::text(&text(&row, key)).contains(&filter))
        {
            if count >= offset && items.len() < page_size {
                if kind == hs::CODES {
                    hs::annotate_year(&mut row, year);
                }
                items.push(row);
            }
            count += 1;
        }
        Ok(())
    })?;
    let pages = (count as usize).div_ceil(page_size).max(1);
    Ok(
        json!({"items":items,"totalCount":count,"pageNumber":page_number,"pageSize":page_size,"totalPages":pages,"hasPreviousPage":page_number>1,"hasNextPage":page_number<pages}),
    )
}

pub(super) fn feedback(
    tx: &Connection,
    normalized: &str,
) -> Result<std::collections::BTreeMap<String, i64>> {
    let mut boosts = std::collections::BTreeMap::<String, i64>::new();
    let fields = [
        "queryText",
        "candidateCode",
        "acceptedCount",
        "rejectedCount",
    ]
    .map(|key| (key, key));
    visit(tx, &mut query(hs::FEEDBACK, &fields), |row| {
        if rules::text(&text(&row, "queryText")) == normalized {
            let boost = row["acceptedCount"]
                .as_i64()
                .unwrap_or(0)
                .saturating_mul(5)
                .saturating_sub(row["rejectedCount"].as_i64().unwrap_or(0).saturating_mul(4));
            let value = boosts.entry(text(&row, "candidateCode")).or_default();
            *value = value.saturating_add(boost);
        }
        Ok(())
    })?;
    Ok(boosts)
}
