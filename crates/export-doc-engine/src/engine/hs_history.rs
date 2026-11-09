//! Authorized, bounded history discovery. Reading candidates never publishes knowledge.
use super::{
    auth,
    error::{Result, invalid},
    hs, hs_queries, hs_search,
    records::text,
    store::{self, Actor, Connection},
};
use export_doc_domain::hs as rules;
use export_doc_storage::GenericQuery;
use serde_json::{Value, json};
use std::collections::{BTreeMap, BTreeSet};

struct Candidate {
    value: Value,
    variants: BTreeSet<String>,
}

pub(super) fn discover(
    tx: &Connection,
    actor: &Actor,
    query: &[(&str, String)],
    year: i32,
) -> Result<Value> {
    let keyword = hs::read(query, "keyword");
    if keyword.chars().count() > 200 {
        return Err(invalid("历史资料检索不能超过 200 字。"));
    }
    let filter = rules::text(keyword);
    let limit = if filter.is_empty() { 2500 } else { 5000 };
    let mut scanned = 0;
    let mut truncated = false;
    let mut groups: BTreeMap<String, Candidate> = BTreeMap::new();
    for (kind, permission) in [
        ("products", "common.product-reference"),
        ("invoices", "document.invoices"),
    ] {
        let mut before = None;
        let product_fields = [
            "nameCN",
            "nameEN",
            "hsCode",
            "material",
            "specification",
            "composition",
            "declarationElements",
            "styleNo",
        ]
        .map(|key| (key, key));
        let fields = if kind == "products" {
            &product_fields[..]
        } else {
            &[("items", "items")][..]
        };
        loop {
            crate::operation::check()?;
            // An invoice can contain thousands of lines: keep the document batch small.
            let (cursor, rows) = tx.scan_generic(
                &GenericQuery {
                    kind,
                    scope: auth::data_scope(actor, permission, "view"),
                    fields,
                    limit: 8,
                    ..Default::default()
                },
                before,
            )?;
            let complete = rows.len() < 8;
            for row in &rows {
                let items = if kind == "products" {
                    std::slice::from_ref(row)
                } else {
                    row["items"]
                        .as_array()
                        .map(Vec::as_slice)
                        .unwrap_or_default()
                };
                for item in items {
                    crate::operation::check()?;
                    let (cn, en) = if kind == "products" {
                        ("nameCN", "nameEN")
                    } else {
                        ("styleNameCN", "styleName")
                    };
                    let mut name = text(item, cn);
                    if name.is_empty() {
                        name = text(item, en);
                    }
                    let spec = [
                        "material",
                        "specification",
                        "composition",
                        "declarationElements",
                    ]
                    .iter()
                    .map(|key| text(item, key))
                    .filter(|s| !s.is_empty())
                    .collect::<Vec<_>>()
                    .join(" ");
                    let raw = text(item, "hsCode");
                    if name.is_empty()
                        || raw.is_empty()
                        || (!filter.is_empty()
                            && !rules::text(&format!("{raw} {name} {spec}")).contains(&filter))
                    {
                        continue;
                    }
                    if scanned >= limit {
                        truncated = true;
                        break;
                    }
                    scanned += 1;
                    let raw = rules::code(&raw).map_err(invalid)?;
                    let fingerprint = rules::fingerprint(&[&raw, &name, &spec]);
                    let group = groups.entry(fingerprint.clone()).or_insert_with(|| Candidate {
                        value: json!({"fingerprint":fingerprint,"rawCode":raw,"productName":name,"specification":spec,"source":if kind=="products" {"商品库"} else {"历史发票"},"sourceCount":0}),
                        variants: BTreeSet::new(),
                    });
                    group.value["sourceCount"] =
                        json!(group.value["sourceCount"].as_i64().unwrap_or(0) + 1);
                    let variant = text(item, "styleNo");
                    if !variant.is_empty() {
                        group.variants.insert(variant);
                    }
                }
                if truncated {
                    break;
                }
            }
            if truncated || complete {
                break;
            }
            before = cursor;
        }
        if truncated {
            break;
        }
    }
    // Only candidate fingerprints remain resident, even with a large knowledge library.
    hs_queries::visit(
        tx,
        &mut hs_queries::query(hs::EXAMPLES, &[("fingerprint", "fingerprint")]),
        |row| {
            groups.remove(&text(&row, "fingerprint"));
            Ok(())
        },
    )?;
    let codes = hs::codes_for_year(tx, year)?;
    let relations = store::matching(tx, hs::REPLACEMENTS, &[], &[])?;
    let mut rows = Vec::with_capacity(groups.len());
    for mut group in groups.into_values() {
        crate::operation::check()?;
        let resolved = hs_search::resolve(
            &text(&group.value, "rawCode"),
            "",
            false,
            &codes,
            &relations,
        );
        let row = &mut group.value;
        row["currentCode"] = json!(resolved.code);
        row["resolutionStatus"] = json!(resolved.status);
        row["replacementCandidates"] = json!(resolved.replacements);
        row["canConfirm"] = json!(resolved.usable);
        row["variantCount"] = json!(group.variants.len());
        row["variantSamples"] = json!(group.variants.into_iter().take(8).collect::<Vec<_>>());
        rows.push(group.value);
    }
    rows.sort_by(|a, b| {
        (a["canConfirm"] != true)
            .cmp(&(b["canConfirm"] != true))
            .then_with(|| b["sourceCount"].as_i64().cmp(&a["sourceCount"].as_i64()))
    });
    let mut page = store::page_only(rows, query);
    page["isTruncated"] = json!(truncated);
    page["scannedSourceCount"] = json!(scanned);
    page["notice"] = json!(if truncated {
        "仅显示受限范围，请缩小检索条件。"
    } else {
        "历史资料只作为待确认候选，不自动改写税则。"
    });
    Ok(page)
}
