//! Bounded HS reads reuse provider ordering and keep the shared semantic matcher.
use super::{error::Result, hs, records::text, store};
use crate::engine::store::Connection;
use export_doc_domain::hs as rules;
use export_doc_storage::HsQuery;
use serde_json::{Value, json};

pub(super) fn visit(
    tx: &Connection,
    query: &mut HsQuery<'_>,
    mut visit: impl FnMut(Value) -> Result<()>,
) -> Result<()> {
    let mut cursor = None;
    loop {
        crate::operation::check()?;
        let (next, rows) = tx.scan_hs(query, cursor.as_ref())?;
        let length = rows.len() as i64;
        for row in rows {
            visit(row)?;
        }
        cursor = next;
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
        since: "",
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
    if filter.is_empty() {
        let mut query = query(kind, &[]);
        query.offset = offset;
        query.limit = page_size as i64;
        let (count, mut items) = tx.page_hs(&query)?;
        if kind == hs::CODES {
            for row in &mut items {
                hs::annotate_year(row, year);
            }
        }
        return Ok(crate::contracts::page(
            items,
            count as usize,
            page_number,
            page_size,
        ));
    }
    let mut count = 0_i64;
    let mut ids = Vec::with_capacity(page_size);
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
    let projection: Vec<_> = std::iter::once(("id", "id"))
        .chain(fields.iter().map(|key| (*key, *key)))
        .collect();
    visit(tx, &mut query(kind, &projection), |row| {
        if fields
            .iter()
            .any(|key| rules::text(&text(&row, key)).contains(&filter))
        {
            if count >= offset && ids.len() < page_size {
                ids.push(
                    row["id"]
                        .as_i64()
                        .ok_or_else(|| super::error::unavailable("HS 记录编号无效。"))?,
                );
            }
            count += 1;
        }
        Ok(())
    })?;
    let mut rows = tx
        .records_by_ids(kind, &ids, &[])?
        .into_iter()
        .map(|row| (row["id"].as_i64().unwrap_or(0), row))
        .collect::<std::collections::HashMap<_, _>>();
    let items = ids
        .into_iter()
        .map(|id| {
            let mut row = rows
                .remove(&id)
                .ok_or_else(|| super::error::unavailable("HS 查询快照记录缺失。"))?;
            if kind == hs::CODES {
                hs::annotate_year(&mut row, year);
            }
            Ok(row)
        })
        .collect::<Result<Vec<_>>>()?;
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
